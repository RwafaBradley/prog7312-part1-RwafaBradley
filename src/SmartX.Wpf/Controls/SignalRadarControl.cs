using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SmartX.Core.Engagement;
using SmartX.Desktop.Client.ViewModels;

namespace SmartX.Wpf.Controls;

public sealed class SignalRadarControl : FrameworkElement
{
    private static readonly DateTime Origin = DateTime.UtcNow;
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(33);
    private const double SweepSeconds = 4.5d;

    private static readonly Pen RingPen = CreatePen("#1D2330", 1d);
    private static readonly Pen SpokePen = CreatePen("#151A24", 1d);
    private static readonly Pen SweepPen = CreatePen("#4FD6C4", 1.4d);
    private static readonly Brush SweepFill = CreateBrush("#4FD6C4");
    private static readonly Brush LabelBrush = CreateBrush("#8C94A6");
    private static readonly Brush CentreBrush = CreateBrush("#4FD6C4");
    private static readonly Typeface LabelFace = new("Consolas");

    private bool _subscribed;
    private DateTime _lastFrame = DateTime.MinValue;
    private readonly List<(RadarPoint Point, Point Position)> _hitMap = new();

    public SignalRadarControl()
    {
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) Subscribe();
            else Unsubscribe();
        };
    }

    private static Pen CreatePen(string hex, double thickness)
    {
        var pen = new Pen(CreateBrush(hex), thickness);
        pen.Freeze();
        return pen;
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(SignalRadarControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty SelectedMacAddressProperty = DependencyProperty.Register(
        nameof(SelectedMacAddress), typeof(string), typeof(SignalRadarControl),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    public string? SelectedMacAddress
    {
        get => (string?)GetValue(SelectedMacAddressProperty);
        set => SetValue(SelectedMacAddressProperty, value);
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SignalRadarControl control) return;

        if (e.OldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= control.OnCollectionChanged;
        }

        if (e.NewValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += control.OnCollectionChanged;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private void Subscribe()
    {
        if (_subscribed || !IsVisible) return;
        CompositionTarget.Rendering += OnRendering;
        _subscribed = true;
    }

    // letting go of the frame clock on the way out, it is shared and would otherwise keep this whole screen alive forever
    private void Unsubscribe()
    {
        if (!_subscribed) return;
        CompositionTarget.Rendering -= OnRendering;
        _subscribed = false;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        if (now - _lastFrame < FrameInterval) return;

        _lastFrame = now;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        var click = e.GetPosition(this);
        RadarPoint? best = null;
        var bestDistance = 18d;

        foreach (var (point, position) in _hitMap)
        {
            var distance = (position - click).Length;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = point;
            }
        }

        if (best is not null)
        {
            SelectedMacAddress = best.MacAddress;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 20d || height <= 20d) return;

        var centre = new Point(width / 2d, height / 2d);
        var maxRadius = Math.Min(width, height) / 2d - 26d;
        if (maxRadius <= 10d) return;

        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

        for (var ring = 1; ring <= 4; ring++)
        {
            var radius = maxRadius * ring / 4d;
            dc.DrawEllipse(null, RingPen, centre, radius, radius);
        }

        for (var spoke = 0; spoke < 8; spoke++)
        {
            var angle = spoke * Math.PI / 4d;
            var edge = new Point(
                centre.X + Math.Cos(angle) * maxRadius,
                centre.Y + Math.Sin(angle) * maxRadius);
            dc.DrawLine(SpokePen, centre, edge);
        }

        var elapsed = (DateTime.UtcNow - Origin).TotalSeconds;
        var sweep = (elapsed % SweepSeconds) / SweepSeconds * Math.PI * 2d;

        const int trailSegments = 20;
        for (var i = 0; i < trailSegments; i++)
        {
            var offset = i * 0.045d;
            var angle = sweep - offset;
            var fade = 1d - (i / (double)trailSegments);

            dc.PushOpacity(0.16d * fade * fade);
            var edge = new Point(
                centre.X + Math.Cos(angle - Math.PI / 2d) * maxRadius,
                centre.Y + Math.Sin(angle - Math.PI / 2d) * maxRadius);
            dc.DrawLine(SweepPen, centre, edge);
            dc.Pop();
        }

        dc.PushOpacity(0.55d);
        var head = new Point(
            centre.X + Math.Cos(sweep - Math.PI / 2d) * maxRadius,
            centre.Y + Math.Sin(sweep - Math.PI / 2d) * maxRadius);
        dc.DrawLine(SweepPen, centre, head);
        dc.Pop();

        dc.DrawEllipse(CentreBrush, null, centre, 3d, 3d);

        _hitMap.Clear();

        if (ItemsSource is null) return;

        foreach (var item in ItemsSource)
        {
            if (item is not RadarPoint point) continue;

            var radius = Math.Clamp(point.Ring, 0.15d, 1d) * maxRadius;
            var position = new Point(
                centre.X + Math.Cos(point.Angle - Math.PI / 2d) * radius,
                centre.Y + Math.Sin(point.Angle - Math.PI / 2d) * radius);

            _hitMap.Add((point, position));

            var delta = Normalise(sweep - point.Angle);
            // a dot is brightest just after the sweep passes it and fades until the next pass, the same as an airport radar
            var freshness = Math.Exp(-delta * 2.4d);

            var accent = BrushFor(point.State);
            var size = 3.2d + (Math.Clamp(point.Amplitude, 0.05d, 1d) * 2.6d);

            if (point.IsAlerting)
            {
                var pulse = 0.5d + (0.5d * Math.Sin(elapsed * 4.5d));
                dc.PushOpacity(0.16d + (0.2d * pulse));
                dc.DrawEllipse(accent, null, position, size * 3.4d, size * 3.4d);
                dc.Pop();
            }

            dc.PushOpacity(0.18d + (0.55d * freshness));
            dc.DrawEllipse(accent, null, position, size * 2.1d, size * 2.1d);
            dc.Pop();

            dc.PushOpacity(0.45d + (0.55d * freshness));
            dc.DrawEllipse(accent, null, position, size, size);
            dc.Pop();

            var isSelected = string.Equals(point.MacAddress, SelectedMacAddress, StringComparison.OrdinalIgnoreCase);

            if (isSelected)
            {
                var selectionPen = new Pen(accent, 1.2d);
                selectionPen.Freeze();
                dc.DrawEllipse(null, selectionPen, position, size * 4d, size * 4d);
            }

            if (point.IsAlerting || isSelected)
            {
                var text = new FormattedText(
                    point.Label,
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    LabelFace,
                    10d,
                    isSelected ? accent : LabelBrush,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);

                dc.DrawText(text, new Point(position.X + (size * 2.2d), position.Y - 7d));
            }
        }
    }

    private static double Normalise(double angle)
    {
        var value = angle % (Math.PI * 2d);
        return value < 0d ? value + (Math.PI * 2d) : value;
    }

    private static Brush BrushFor(RhythmState state) => state switch
    {
        RhythmState.Healthy => Converters.Palette.Teal,
        RhythmState.Drifting => Converters.Palette.Amber,
        RhythmState.Flaring => Converters.Palette.Rose,
        RhythmState.Stalled => Converters.Palette.Violet,
        RhythmState.Flatlined => Converters.Palette.Slate,
        _ => Converters.Palette.Faint
    };

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 420d : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? 420d : availableSize.Height;
        return new Size(width, height);
    }
}
