using SmartX.Core.Domain;

namespace SmartX.Core.Engagement;

// these five states are what the screen will colour, the window only reads them and never works them out itself
public enum RhythmState
{
    Unknown = 0,

    Healthy = 1,

    Drifting = 2,

    Flaring = 3,

    Stalled = 4,

    Flatlined = 5
}

public sealed class SensorVitals
{
    public string MacAddress { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public SensorCategory Category { get; set; }

    public string LocationPath { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public RhythmState State { get; set; } = RhythmState.Unknown;

    public double Bpm { get; set; }

    public double Amplitude { get; set; }

    public double LatestValue { get; set; }

    public double Mean { get; set; }

    public double StandardDeviation { get; set; }

    public double ZScore { get; set; }

    public double SmoothedZScore { get; set; }

    public double SilenceMs { get; set; }

    public double ExpectedIntervalMs { get; set; }

    public double JitterMs { get; set; }

    public long PacketsAccepted { get; set; }

    public long PacketsRejected { get; set; }

    public DateTimeOffset? LastSeenUtc { get; set; }

    public double[] Trace { get; set; } = Array.Empty<double>();

    public string Explanation { get; set; } = string.Empty;

    public double Urgency { get; set; }
}

public sealed class HeartbeatSnapshot
{
    public DateTimeOffset GeneratedUtc { get; set; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<SensorVitals> Tiles { get; set; } = Array.Empty<SensorVitals>();

    public double MeshBpm { get; set; }

    public int Healthy { get; set; }

    public int Drifting { get; set; }

    public int Flaring { get; set; }

    public int Stalled { get; set; }

    public int Flatlined { get; set; }

    public int TotalNodes { get; set; }

    public long TotalAccepted { get; set; }

    public long TotalRejected { get; set; }

    public double MeshIntegrity { get; set; }
}

public sealed class HeartbeatWall
{
    private const double FlareThreshold = 3.0d;
    private const double DriftThreshold = 2.0d;
    private const int TraceLength = 40;

    private const int StallRepeats = 10;

    private static readonly TimeSpan FlareHold = TimeSpan.FromSeconds(8);

    private readonly Dictionary<string, NodeMonitor> _monitors = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public double FlatlineIntervals { get; init; } = 3.5d;

    public void Track(SensorRegistration registration)
    {
        lock (_gate)
        {
            if (!_monitors.TryGetValue(registration.MacAddress, out var monitor))
            {
                monitor = new NodeMonitor();
                _monitors[registration.MacAddress] = monitor;
            }

            monitor.Registration = registration;
        }
    }

    public void Forget(string macAddress)
    {
        lock (_gate)
        {
            _monitors.Remove(macAddress);
        }
    }

    public void Observe(string macAddress, double magnitude, DateTimeOffset timestampUtc)
    {
        lock (_gate)
        {
            if (!_monitors.TryGetValue(macAddress, out var monitor))
            {
                monitor = new NodeMonitor();
                _monitors[macAddress] = monitor;
            }

            monitor.Observe(magnitude, timestampUtc);
        }
    }

    public void ObserveRejection(string macAddress)
    {
        lock (_gate)
        {
            if (_monitors.TryGetValue(macAddress, out var monitor))
            {
                monitor.Rejected++;
            }
        }
    }

    public HeartbeatSnapshot Snapshot(DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            var tiles = new List<SensorVitals>(_monitors.Count);
            var snapshot = new HeartbeatSnapshot { GeneratedUtc = nowUtc };

            foreach (var (mac, monitor) in _monitors)
            {
                var vitals = monitor.ToVitals(mac, nowUtc, FlatlineIntervals);
                tiles.Add(vitals);

                snapshot.TotalAccepted += vitals.PacketsAccepted;
                snapshot.TotalRejected += vitals.PacketsRejected;
                snapshot.MeshBpm += vitals.Bpm;

                switch (vitals.State)
                {
                    case RhythmState.Healthy: snapshot.Healthy++; break;
                    case RhythmState.Drifting: snapshot.Drifting++; break;
                    case RhythmState.Flaring: snapshot.Flaring++; break;
                    case RhythmState.Stalled: snapshot.Stalled++; break;
                    case RhythmState.Flatlined: snapshot.Flatlined++; break;
                }
            }

            tiles.Sort(static (a, b) => b.Urgency.CompareTo(a.Urgency));

            snapshot.Tiles = tiles;
            snapshot.TotalNodes = tiles.Count;
            snapshot.MeshIntegrity = tiles.Count == 0
                ? 1d
                : Math.Clamp(1d - ((snapshot.Flatlined * 1.0d) + (snapshot.Flaring * 0.6d) +
                                   (snapshot.Stalled * 0.5d) + (snapshot.Drifting * 0.25d)) / tiles.Count, 0d, 1d);

            return snapshot;
        }
    }

    private sealed class NodeMonitor
    {
        private readonly RunningStatistics _valueStats = new();
        private readonly RunningStatistics _intervalStats = new();
        private readonly double[] _trace = new double[TraceLength];
        private int _traceHead;
        private int _traceCount;

        public SensorRegistration? Registration { get; set; }

        public long Accepted { get; private set; }

        public long Rejected { get; set; }

        public double LatestValue { get; private set; }

        public double LatestZ { get; private set; }

        public double SmoothedZ { get; private set; }

        public DateTimeOffset? LastFlareUtc { get; private set; }

        public double PeakZ { get; private set; }

        public double PeakValue { get; private set; }

        public DateTimeOffset? LastSeenUtc { get; private set; }

        public void Observe(double magnitude, DateTimeOffset timestampUtc)
        {
            if (LastSeenUtc is { } previous)
            {
                var gap = (timestampUtc - previous).TotalMilliseconds;
                if (gap is > 0d and < 3_600_000d)
                {
                    _intervalStats.Add(gap);
                }
            }

            LatestZ = _valueStats.ZScore(magnitude);
            // one odd reading is noise, a run of them is drift, so the warning waits for the average to move
            SmoothedZ = (SmoothedZ * 0.82d) + (Math.Abs(LatestZ) * 0.18d);

            if (Math.Abs(LatestZ) >= FlareThreshold)
            {
                LastFlareUtc = timestampUtc;
                PeakZ = LatestZ;
                PeakValue = magnitude;
            }

            _valueStats.Add(magnitude);

            LatestValue = magnitude;
            LastSeenUtc = timestampUtc;
            Accepted++;

            _trace[_traceHead] = magnitude;
            _traceHead = (_traceHead + 1) % TraceLength;
            if (_traceCount < TraceLength) _traceCount++;
        }

        public SensorVitals ToVitals(string mac, DateTimeOffset nowUtc, double flatlineIntervals)
        {
            var expected = _intervalStats.Count > 2d ? _intervalStats.Mean : 1_000d;
            var silence = LastSeenUtc is { } seen ? (nowUtc - seen).TotalMilliseconds : double.PositiveInfinity;
            var absZ = Math.Abs(LatestZ);

            var vitals = new SensorVitals
            {
                MacAddress = mac,
                DisplayName = Registration?.DisplayName ?? mac,
                Category = Registration?.Category ?? SensorCategory.Environmental,
                LocationPath = Registration?.Location.Path ?? string.Empty,
                Unit = Registration?.Unit ?? string.Empty,
                LatestValue = LatestValue,
                Mean = _valueStats.Mean,
                StandardDeviation = _valueStats.StandardDeviation,
                ZScore = LatestZ,
                SmoothedZScore = SmoothedZ,
                PacketsAccepted = Accepted,
                PacketsRejected = Rejected,
                LastSeenUtc = LastSeenUtc,
                ExpectedIntervalMs = expected,
                JitterMs = _intervalStats.StandardDeviation,
                SilenceMs = double.IsInfinity(silence) ? -1d : silence,
                Trace = SnapshotTrace()
            };

            vitals.Bpm = expected > 0d ? Math.Clamp(60_000d / expected, 0d, 600d) : 0d;

            vitals.Amplitude = Math.Clamp(absZ / 4d, 0.05d, 1d);

            if (Accepted == 0)
            {
                vitals.State = RhythmState.Unknown;
                vitals.Bpm = 0d;
                vitals.Amplitude = 0.05d;
                vitals.Explanation = "Registered but has not published yet.";
            }
            else if (silence > expected * flatlineIntervals && silence > 2_000d)
            {
                vitals.State = RhythmState.Flatlined;
                vitals.Bpm = 0d;
                vitals.Amplitude = 0d;
                vitals.Explanation =
                    $"Silent for {silence / 1000d:F1} s against an expected {expected / 1000d:F1} s cadence — treat as disconnected.";
            }
            else if (Registration?.Category == SensorCategory.Actuator)
            {
                var transitions = CountTransitions(vitals.Trace);
                vitals.Amplitude = Math.Clamp(transitions / 12d, 0.05d, 1d);

                if (transitions >= 12)
                {
                    vitals.State = RhythmState.Flaring;
                    vitals.Explanation =
                        $"{transitions} state changes in the last {vitals.Trace.Length} samples — the relay is chattering.";
                }
                else if (transitions >= 7)
                {
                    vitals.State = RhythmState.Drifting;
                    vitals.Explanation =
                        $"{transitions} state changes in the last {vitals.Trace.Length} samples — switching more than expected.";
                }
                else
                {
                    vitals.State = RhythmState.Healthy;
                    vitals.Explanation =
                        $"Holding {(LatestValue >= 0.5d ? "open" : "closed")}; {transitions} state changes in the window.";
                }
            }
            else if (RepeatedTail(vitals.Trace) is var run && run >= StallRepeats)
            {
                vitals.State = RhythmState.Stalled;
                vitals.Amplitude = 0.08d;
                vitals.Explanation =
                    $"Reporting on cadence but the last {run} readings are all exactly {LatestValue:F2} — suspect a wedged sensor.";
            }
            else if (absZ >= FlareThreshold)
            {
                vitals.State = RhythmState.Flaring;
                vitals.Explanation =
                    $"Latest reading {LatestValue:F2} is {absZ:F1} sigma from its own {_valueStats.Mean:F2} baseline.";
            }
            else if (LastFlareUtc is { } flare && nowUtc - flare <= FlareHold)
            {
                vitals.State = RhythmState.Flaring;
                vitals.Amplitude = Math.Clamp(Math.Abs(PeakZ) / 4d, 0.3d, 1d);
                vitals.Explanation =
                    $"Spiked to {PeakValue:F2} ({Math.Abs(PeakZ):F1} sigma) {(nowUtc - flare).TotalSeconds:F0} s ago; reading has since recovered.";
            }
            else if (SmoothedZ >= DriftThreshold || IsJittering(expected))
            {
                vitals.State = RhythmState.Drifting;
                vitals.Explanation = SmoothedZ >= DriftThreshold
                    ? $"Sustained wander — averaging {SmoothedZ:F1} sigma off baseline, currently {absZ:F1}."
                    : $"Arrival jitter of {_intervalStats.StandardDeviation:F0} ms against a {expected:F0} ms cadence — unstable link.";
            }
            else
            {
                vitals.State = RhythmState.Healthy;
                vitals.Explanation = $"On cadence at {vitals.Bpm:F0} packets/min, inside the expected envelope.";
            }

            vitals.Urgency = Urgency(vitals, absZ, silence, expected);
            return vitals;
        }

        private static int RepeatedTail(IReadOnlyList<double> trace)
        {
            if (trace.Count == 0) return 0;

            var last = trace[^1];
            var run = 1;

            for (var i = trace.Count - 2; i >= 0; i--)
            {
                if (Math.Abs(trace[i] - last) > 1e-9d) break;
                run++;
            }

            return run;
        }

        private static int CountTransitions(IReadOnlyList<double> trace)
        {
            var transitions = 0;

            for (var i = 1; i < trace.Count; i++)
            {
                if (trace[i] >= 0.5d != trace[i - 1] >= 0.5d)
                {
                    transitions++;
                }
            }

            return transitions;
        }

        private bool IsJittering(double expected)
            => _intervalStats.Count >= 10d
               && expected > 0d
               && _intervalStats.StandardDeviation > expected * 0.6d;

        private static double Urgency(SensorVitals vitals, double absZ, double silence, double expected)
        {
            var baseScore = vitals.State switch
            {
                RhythmState.Flatlined => 100d,
                RhythmState.Flaring => 80d,
                RhythmState.Stalled => 60d,
                RhythmState.Drifting => 40d,
                RhythmState.Unknown => 20d,
                _ => 0d
            };

            var magnitude = Math.Min(absZ, 10d);
            var silencePenalty = double.IsInfinity(silence) || expected <= 0d
                ? 0d
                : Math.Min(silence / Math.Max(expected, 1d), 20d);

            return baseScore + magnitude + silencePenalty + Math.Min(vitals.PacketsRejected * 0.5d, 10d);
        }

        private double[] SnapshotTrace()
        {
            var result = new double[_traceCount];
            var start = (_traceHead - _traceCount + TraceLength) % TraceLength;

            for (var i = 0; i < _traceCount; i++)
            {
                result[i] = _trace[(start + i) % TraceLength];
            }

            return result;
        }
    }
}
