using SmartX.Core.Domain;
using SmartX.Core.Engagement;
using SmartX.Desktop.Client.Mvvm;

namespace SmartX.Desktop.Client.ViewModels;

public sealed class SensorTileViewModel : ObservableObject
{
    private RhythmState _state;
    private double _bpm;
    private double _amplitude;
    private double _latestValue;
    private double _mean;
    private double _standardDeviation;
    private double _zScore;
    private double _smoothedZScore;
    private double _silenceMs;
    private double _jitterMs;
    private long _packetsAccepted;
    private long _packetsRejected;
    private string _explanation = string.Empty;
    private string _displayName = string.Empty;
    private string _locationPath = string.Empty;
    private string _unit = string.Empty;
    private double _urgency;
    private IReadOnlyList<double> _trace = Array.Empty<double>();
    private SensorCategory _category;

    public SensorTileViewModel(SensorVitals vitals)
    {
        MacAddress = vitals.MacAddress;
        Apply(vitals);
    }

    public string MacAddress { get; }

    public string DisplayName { get => _displayName; private set => Set(ref _displayName, value); }

    public SensorCategory Category { get => _category; private set => Set(ref _category, value); }

    public string LocationPath { get => _locationPath; private set => Set(ref _locationPath, value); }

    public string Unit { get => _unit; private set => Set(ref _unit, value); }

    public RhythmState State
    {
        get => _state;
        private set
        {
            if (Set(ref _state, value))
            {
                Raise(nameof(IsBeating));
                Raise(nameof(StateLabel));
            }
        }
    }

    public string StateLabel => State.ToString().ToUpperInvariant();

    public double Bpm
    {
        get => _bpm;
        private set
        {
            if (Set(ref _bpm, value))
            {
                Raise(nameof(BeatSeconds));
                Raise(nameof(BpmDisplay));
                Raise(nameof(IsBeating));
            }
        }
    }

    public double BeatSeconds => Bpm > 0d ? Math.Clamp(60d / Bpm, 0.28d, 4d) : 0d;

    public string BpmDisplay => Bpm > 0d ? $"{Math.Round(Bpm)}/min" : "— — —";

    public bool IsBeating => State != RhythmState.Flatlined && Bpm > 0d;

    public double Amplitude { get => _amplitude; private set => Set(ref _amplitude, value); }

    public double LatestValue
    {
        get => _latestValue;
        private set { if (Set(ref _latestValue, value)) Raise(nameof(ValueDisplay)); }
    }

    public string ValueDisplay => Category switch
    {
        SensorCategory.Actuator => LatestValue >= 0.5d ? "OPEN" : "CLOSED",
        SensorCategory.PowerConsumption => $"{Math.Round(LatestValue)} {Unit}",
        _ => $"{LatestValue:F2} {Unit}"
    };

    public double Mean { get => _mean; private set => Set(ref _mean, value); }

    public double StandardDeviation { get => _standardDeviation; private set => Set(ref _standardDeviation, value); }

    public double ZScore { get => _zScore; private set => Set(ref _zScore, value); }

    public double SmoothedZScore { get => _smoothedZScore; private set => Set(ref _smoothedZScore, value); }

    public double SilenceMs { get => _silenceMs; private set => Set(ref _silenceMs, value); }

    public double JitterMs { get => _jitterMs; private set => Set(ref _jitterMs, value); }

    public long PacketsAccepted { get => _packetsAccepted; private set => Set(ref _packetsAccepted, value); }

    public long PacketsRejected { get => _packetsRejected; private set => Set(ref _packetsRejected, value); }

    public string Explanation { get => _explanation; private set => Set(ref _explanation, value); }

    public double Urgency { get => _urgency; private set => Set(ref _urgency, value); }

    public IReadOnlyList<double> Trace
    {
        get => _trace;
        private set { _trace = value; Raise(); }
    }

    // tiles are changed in place rather than rebuilt, swapping the whole list every second restarts every animation on screen
    public void Apply(SensorVitals vitals)
    {
        DisplayName = vitals.DisplayName;
        Category = vitals.Category;
        LocationPath = vitals.LocationPath;
        Unit = vitals.Unit;
        State = vitals.State;
        Bpm = vitals.Bpm;
        Amplitude = vitals.Amplitude;
        LatestValue = vitals.LatestValue;
        Mean = vitals.Mean;
        StandardDeviation = vitals.StandardDeviation;
        ZScore = vitals.ZScore;
        SmoothedZScore = vitals.SmoothedZScore;
        SilenceMs = vitals.SilenceMs;
        JitterMs = vitals.JitterMs;
        PacketsAccepted = vitals.PacketsAccepted;
        PacketsRejected = vitals.PacketsRejected;
        Explanation = vitals.Explanation;
        Urgency = vitals.Urgency;
        Trace = vitals.Trace;
    }
}
