using SmartX.Core.Domain;
using SmartX.Core.Ingestion;
using SmartX.Core.Telemetry;

namespace SmartX.Core.Simulation;

public sealed class TelemetrySimulator
{
    private readonly IngestionGateway _gateway;
    private readonly Random _random;
    private readonly Dictionary<string, NodeProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private long _tick;

    public TelemetrySimulator(IngestionGateway gateway, int seed = 20260913)
    {
        _gateway = gateway;
        _random = new Random(seed);
    }

    // faults are injected on purpose, otherwise there is nothing for the detector to catch
    public double FaultRate { get; set; } = 0.005d;

    public IReadOnlyList<SensorRegistration> SeedMesh(int environmental = 9, int power = 5, int actuators = 4)
    {
        var created = new List<SensorRegistration>();

        created.AddRange(Seed(environmental, SensorCategory.Environmental));
        created.AddRange(Seed(power, SensorCategory.PowerConsumption));
        created.AddRange(Seed(actuators, SensorCategory.Actuator));

        return created;
    }

    private IEnumerable<SensorRegistration> Seed(int count, SensorCategory category)
    {
        var facilities = new[] { "Facility A", "Facility B" };
        var zones = new[] { "Zone 1", "Zone 2", "Zone 3" };
        var subZones = new[] { "Sub-Zone A", "Sub-Zone B", "Sub-Zone C" };

        for (var i = 0; i < count; i++)
        {
            var mac = NextMac();
            var (metric, unit, min, max) = Envelope(category);

            var registration = new SensorRegistration
            {
                MacAddress = mac,
                Alias = $"{Prefix(category)}-{i + 1:D2}",
                Category = category,
                Unit = unit,
                MinExpected = min,
                MaxExpected = max,
                FirmwareVersion = $"esp32-idf-5.2.{_random.Next(0, 4)}",
                Location = new DeploymentLocation
                {
                    Facility = facilities[_random.Next(facilities.Length)],
                    Zone = zones[_random.Next(zones.Length)],
                    SubZone = subZones[_random.Next(subZones.Length)],
                    NodeId = $"{Prefix(category)}-{i + 1:D2}"
                }
            };

            var (ok, _, stored) = _gateway.Register(registration);
            if (!ok) continue;

            lock (_gate)
            {
                _profiles[stored.MacAddress] = new NodeProfile
                {
                    Category = category,
                    Metric = metric,
                    Unit = unit,
                    Centre = (min + max) / 2d,
                    Swing = (max - min) / 4d,
                    Phase = _random.NextDouble() * Math.PI * 2d,
                    PeriodTicks = 120 + _random.Next(0, 240),
                    NoiseSigma = Math.Max((max - min) * 0.012d, 0.05d),
                    Min = min,
                    Max = max
                };
            }

            yield return stored;
        }
    }

    public int Tick(DateTimeOffset? timestampUtc = null, bool allowFaults = true)
    {
        var tick = Interlocked.Increment(ref _tick);
        var stamp = timestampUtc ?? DateTimeOffset.UtcNow;
        var envelopes = new List<TelemetryEnvelope>();

        KeyValuePair<string, NodeProfile>[] profiles;
        lock (_gate)
        {
            profiles = _profiles.ToArray();
        }

        foreach (var (mac, profile) in profiles)
        {
            if (allowFaults)
            {
                MaybeInjectFault(profile);
            }

            if (profile.SilentTicksRemaining > 0)
            {
                profile.SilentTicksRemaining--;
                continue;
            }

            var value = profile.Next(tick, _random);

            envelopes.Add(new TelemetryEnvelope
            {
                MacAddress = mac,
                Metric = profile.Metric,
                Category = profile.Category,
                Kind = profile.Category.ValueKind(),
                Numeric = value,
                Unit = profile.Unit,
                Sequence = profile.Sequence++,
                TimestampUtc = stamp
            });
        }

        var results = _gateway.IngestBatch(envelopes);
        return results.Count(r => r.Accepted);
    }

    public bool InjectFault(string macAddress, string faultKind)
    {
        lock (_gate)
        {
            if (!_profiles.TryGetValue(macAddress, out var profile))
            {
                return false;
            }

            switch (faultKind.ToLowerInvariant())
            {
                case "spike":

                    profile.SpikeTicksRemaining = 6;
                    break;
                case "disconnect":
                    profile.SilentTicksRemaining = 25;
                    break;
                case "stall":
                    profile.StallTicksRemaining = 40;
                    profile.StallValue = profile.LastValue;
                    break;
                case "clear":
                    profile.SpikeTicksRemaining = 0;
                    profile.SilentTicksRemaining = 0;
                    profile.StallTicksRemaining = 0;
                    break;
                default:
                    return false;
            }

            return true;
        }
    }

    private void MaybeInjectFault(NodeProfile profile)
    {
        if (profile.SpikeTicksRemaining > 0 || profile.SilentTicksRemaining > 0 || profile.StallTicksRemaining > 0)
        {
            return;
        }

        if (_random.NextDouble() >= FaultRate)
        {
            return;
        }

        switch (_random.Next(0, 3))
        {
            case 0: profile.SpikeTicksRemaining = _random.Next(2, 5); break;
            case 1: profile.SilentTicksRemaining = _random.Next(12, 30); break;
            default:
                profile.StallTicksRemaining = _random.Next(20, 45);
                profile.StallValue = profile.LastValue;
                break;
        }
    }

    public IReadOnlyList<string> KnownNodes()
    {
        lock (_gate) { return _profiles.Keys.ToArray(); }
    }

    private string NextMac()
    {
        Span<byte> tail = stackalloc byte[3];
        _random.NextBytes(tail);
        return $"24:6F:28:{tail[0]:X2}:{tail[1]:X2}:{tail[2]:X2}";
    }

    private static string Prefix(SensorCategory category) => category switch
    {
        SensorCategory.Environmental => "ENV",
        SensorCategory.PowerConsumption => "PWR",
        SensorCategory.Actuator => "ACT",
        _ => "NODE"
    };

    private static (string Metric, string Unit, double Min, double Max) Envelope(SensorCategory category)
        => category switch
        {
            SensorCategory.Environmental => ("soil_moisture", "%VWC", 12d, 68d),
            SensorCategory.PowerConsumption => ("active_power", "W", 0d, 2_400d),
            SensorCategory.Actuator => ("valve_state", "state", 0d, 1d),
            _ => ("value", string.Empty, 0d, 100d)
        };

    private sealed class NodeProfile
    {
        public SensorCategory Category { get; init; }

        public string Metric { get; init; } = string.Empty;

        public string Unit { get; init; } = string.Empty;

        public double Centre { get; init; }

        public double Swing { get; init; }

        public double Phase { get; init; }

        public int PeriodTicks { get; init; } = 180;

        public double NoiseSigma { get; init; } = 0.5d;

        public double Min { get; init; }

        public double Max { get; init; }

        public long Sequence { get; set; }

        public double LastValue { get; set; }

        public int SpikeTicksRemaining { get; set; }

        public int SilentTicksRemaining { get; set; }

        public int StallTicksRemaining { get; set; }

        public double StallValue { get; set; }

        public double Next(long tick, Random random)
        {
            if (StallTicksRemaining > 0)
            {
                StallTicksRemaining--;
                LastValue = StallValue;
                return LastValue;
            }

            double value;

            if (Category == SensorCategory.Actuator)
            {
                var flip = random.NextDouble() < 0.06d;
                value = flip ? (LastValue >= 0.5d ? 0d : 1d) : LastValue;

                if (SpikeTicksRemaining > 0)
                {
                    SpikeTicksRemaining--;
                    value = LastValue >= 0.5d ? 0d : 1d;
                }

                LastValue = value;
                return value;
            }

            var angle = (2d * Math.PI * tick / PeriodTicks) + Phase;
            value = Centre + (Swing * Math.Sin(angle)) + Gaussian(random, NoiseSigma);

            if (SpikeTicksRemaining > 0)
            {
                SpikeTicksRemaining--;
                var direction = random.NextDouble() < 0.5d ? -1d : 1d;
                value += direction * Swing * (2.5d + random.NextDouble() * 1.5d);
            }

            value = Math.Clamp(value, Min - (Max - Min) * 0.3d, Max + (Max - Min) * 0.3d);

            if (Category == SensorCategory.PowerConsumption)
            {
                value = Math.Round(Math.Max(0d, value));
            }

            LastValue = value;
            return value;
        }

        // real sensor noise clusters near the middle rather than spreading evenly, so it is made that way here
        private static double Gaussian(Random random, double sigma)
        {
            var u1 = 1d - random.NextDouble();
            var u2 = 1d - random.NextDouble();
            return sigma * Math.Sqrt(-2d * Math.Log(u1)) * Math.Sin(2d * Math.PI * u2);
        }
    }
}
