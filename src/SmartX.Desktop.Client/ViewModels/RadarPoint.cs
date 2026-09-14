using SmartX.Core.Domain;
using SmartX.Core.Engagement;
using SmartX.Desktop.Client.Mvvm;

namespace SmartX.Desktop.Client.ViewModels;

public sealed class RadarPoint : ObservableObject
{
    private double _angle;
    private double _ring;
    private double _amplitude;
    private RhythmState _state;
    private double _value;

    public RadarPoint(string macAddress, string label, string zone)
    {
        MacAddress = macAddress;
        Label = label;
        Zone = zone;
    }

    public string MacAddress { get; }

    public string Label { get; }

    public string Zone { get; }

    public SensorCategory Category { get; set; }

    public double Angle
    {
        get => _angle;
        set => Set(ref _angle, value);
    }

    // where the dot sits, the angle is its place inside a zone and the ring is which zone it belongs to
    public double Ring
    {
        get => _ring;
        set => Set(ref _ring, value);
    }

    public double Amplitude
    {
        get => _amplitude;
        set => Set(ref _amplitude, value);
    }

    public RhythmState State
    {
        get => _state;
        set => Set(ref _state, value);
    }

    public double Value
    {
        get => _value;
        set => Set(ref _value, value);
    }

    public bool IsAlerting => State is RhythmState.Flaring or RhythmState.Flatlined or RhythmState.Stalled;
}

public sealed class AlertViewModel : ObservableObject
{
    public AlertViewModel(SensorVitals vitals)
    {
        MacAddress = vitals.MacAddress;
        Label = vitals.DisplayName;
        Zone = vitals.LocationPath;
        State = vitals.State;
        Detail = vitals.Explanation;
        Urgency = vitals.Urgency;
    }

    public string MacAddress { get; }

    public string Label { get; }

    public string Zone { get; }

    public RhythmState State { get; }

    public string Detail { get; }

    public double Urgency { get; }

    public string StateLabel => State.ToString().ToUpperInvariant();
}

public sealed class DeploymentRowViewModel
{
    public DeploymentRowViewModel(string name, string kind, int depth, string? macAddress, bool isDevice)
    {
        Name = name;
        Kind = kind;
        Depth = depth;
        MacAddress = macAddress;
        IsDevice = isDevice;
    }

    public string Name { get; }

    public string Kind { get; }

    public int Depth { get; }

    public string? MacAddress { get; }

    public bool IsDevice { get; }

    public double Indent => Depth * 22d;

    public string DepthLabel => Depth == 0 ? "root" : IsDevice ? "leaf" : $"depth {Depth}";

    public string Glyph => Depth == 0 ? "•" : "└";

    public bool Checked { get; set; }
}

public sealed class TraceStepViewModel
{
    public TraceStepViewModel(string call, string note, int depth)
    {
        Call = call;
        Note = note;
        Depth = depth;
    }

    public string Call { get; }

    public string Note { get; }

    public int Depth { get; }

    public double Indent => Depth * 14d;
}

public sealed class TypedSampleViewModel : ObservableObject
{
    private string _display = "waiting";
    private string _raw = string.Empty;

    public TypedSampleViewModel(string typeKeyword, string categoryName, string accentKey)
    {
        TypeKeyword = typeKeyword;
        CategoryName = categoryName;
        AccentKey = accentKey;
    }

    public string TypeKeyword { get; }

    public string CategoryName { get; }

    public string AccentKey { get; }

    public string Display
    {
        get => _display;
        set => Set(ref _display, value);
    }

    public string Raw
    {
        get => _raw;
        set => Set(ref _raw, value);
    }
}
