using SmartX.Core.Arrays;
using SmartX.Core.Collections;
using SmartX.Core.Domain;
using SmartX.Core.Engagement;
using SmartX.Core.Telemetry;
using SmartX.Core.Topology;
using SmartX.Core.Validation;

namespace SmartX.Core.Ingestion;

public sealed class IngestionGateway
{
    private readonly Dictionary<string, ITelemetryChannel> _channels = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _channelGate = new();

    private long _accepted;
    private long _rejected;

    public IngestionGateway(int windowSize = 512)
    {
        WindowSize = windowSize;
        Registry = new SensorRegistry();
        Wall = new HeartbeatWall();
        Topology = SeedTopologyRoot();
        RenderFrame = new TelemetryBatchMatrix(maxBatches: 16, nodeSlots: 128, timeSlots: 60);
    }

    public int WindowSize { get; }

    public SensorRegistry Registry { get; }

    public HeartbeatWall Wall { get; }

    public DeploymentNode Topology { get; private set; }

    public TelemetryBatchMatrix RenderFrame { get; }

    public long AcceptedCount => Interlocked.Read(ref _accepted);

    public long RejectedCount => Interlocked.Read(ref _rejected);

    public DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;

    public (bool Ok, IReadOnlyList<string> Errors, SensorRegistration Registration) Register(SensorRegistration registration)
    {
        registration.MacAddress = MacAddressRules.IsValid(registration.MacAddress)
            ? MacAddressRules.Normalise(registration.MacAddress)
            : registration.MacAddress?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(registration.Unit))
        {
            registration.Unit = registration.Category.DefaultUnit();
        }

        var errors = PacketValidator.ValidateRegistration(registration);
        if (errors.Count > 0)
        {
            return (false, errors, registration);
        }

        Registry.Register(registration);
        Wall.Track(registration);

        lock (_channelGate)
        {
            if (!_channels.TryGetValue(registration.MacAddress, out var existing) ||
                existing.ValueKind != registration.ValueKind)
            {
                _channels[registration.MacAddress] = CreateChannel(registration);
            }
        }

        AttachToTopology(registration);
        return (true, Array.Empty<string>(), registration);
    }

    // the type decision is made here once when a node signs up, and never again per packet
    private ITelemetryChannel CreateChannel(SensorRegistration registration) => registration.Category switch
    {
        SensorCategory.Environmental => new TelemetryChannel<float>(registration, WindowSize),
        SensorCategory.PowerConsumption => new TelemetryChannel<int>(registration, WindowSize),
        SensorCategory.Actuator => new TelemetryChannel<bool>(registration, WindowSize),
        _ => throw new ArgumentOutOfRangeException(nameof(registration), registration.Category, "Unsupported category.")
    };

    public bool Remove(string macAddress)
    {
        lock (_channelGate)
        {
            _channels.Remove(macAddress);
        }

        Wall.Forget(macAddress);
        return Registry.Remove(macAddress);
    }

    public bool TryGetChannel(string macAddress, out ITelemetryChannel channel)
    {
        lock (_channelGate)
        {
            return _channels.TryGetValue(macAddress ?? string.Empty, out channel!);
        }
    }

    // the front desk, find the node, check the packet, hand it to that node own pipe
    public IngestionResult Ingest(TelemetryEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        envelope.MacAddress = MacAddressRules.IsValid(envelope.MacAddress)
            ? MacAddressRules.Normalise(envelope.MacAddress)
            : envelope.MacAddress ?? string.Empty;

        if (!Registry.TryGet(envelope.MacAddress, out var registration))
        {
            Interlocked.Increment(ref _rejected);
            Wall.ObserveRejection(envelope.MacAddress);

            return IngestionResult.Fail(
                envelope.MacAddress,
                new[] { $"Node '{envelope.MacAddress}' is not registered with this gateway." },
                AcceptedCount,
                RejectedCount);
        }

        if (!TryGetChannel(envelope.MacAddress, out var channel))
        {
            Interlocked.Increment(ref _rejected);
            return IngestionResult.Fail(
                envelope.MacAddress,
                new[] { "No ingestion channel is open for this node." },
                AcceptedCount,
                RejectedCount);
        }

        envelope.Category = registration.Category;
        if (envelope.Kind == default && registration.ValueKind != default)
        {
            envelope.Kind = registration.ValueKind;
        }

        var errors = PacketValidator.ValidatePacket(registration, envelope, channel.LastSequence);
        if (errors.Count > 0)
        {
            Interlocked.Increment(ref _rejected);
            Wall.ObserveRejection(envelope.MacAddress);
            return IngestionResult.Fail(envelope.MacAddress, errors, AcceptedCount, RejectedCount);
        }

        channel.Accept(envelope);
        Wall.Observe(envelope.MacAddress, envelope.Numeric, envelope.TimestampUtc);
        Interlocked.Increment(ref _accepted);

        return IngestionResult.Ok(
            envelope.MacAddress,
            $"Accepted on the {registration.Category} channel.",
            AcceptedCount,
            RejectedCount);
    }

    public IReadOnlyList<IngestionResult> IngestBatch(IEnumerable<TelemetryEnvelope> envelopes)
    {
        var results = new List<IngestionResult>();

        foreach (var envelope in envelopes)
        {
            results.Add(Ingest(envelope));
        }

        PushRenderColumn();
        return results;
    }

    public void PushRenderColumn()
    {
        var order = new List<string>();
        var magnitudes = new List<double>();

        foreach (var registration in Registry.Snapshot())
        {
            order.Add(registration.MacAddress);
            magnitudes.Add(TryGetChannel(registration.MacAddress, out var channel)
                ? channel.LatestMagnitude
                : double.NaN);
        }

        RenderFrame.PushFrameColumn(order, magnitudes);
    }

    public IReadOnlyList<TelemetryEnvelope> History(string macAddress, int take = 120)
        => TryGetChannel(macAddress, out var channel)
            ? channel.History(take)
            : Array.Empty<TelemetryEnvelope>();

    public HeartbeatSnapshot Heartbeat() => Wall.Snapshot(DateTimeOffset.UtcNow);

    public (double Aggregate, double Delta, string Unit, string Detail) AggregatePair(string macA, string macB)
    {
        if (!TryGetChannel(macA, out var a) || !TryGetChannel(macB, out var b))
        {
            return (0d, 0d, string.Empty, "One or both nodes are unknown to the gateway.");
        }

        if (a is TelemetryChannel<int> meterA && b is TelemetryChannel<int> meterB)
        {
            var meter1 = meterA.Window().LastOrDefault();
            var meter2 = meterB.Window().LastOrDefault();
            var meter3 = meter1 + meter2;
            var drift = meter1 - meter2;

            return (meter3.Magnitude, drift.Magnitude, meter3.Unit,
                $"{meter3} computed with the overloaded + operator on TelemetryPacket<int>.");
        }

        if (a is TelemetryChannel<float> envA && b is TelemetryChannel<float> envB)
        {
            var left = envA.Window().LastOrDefault();
            var right = envB.Window().LastOrDefault();
            var sum = left + right;
            var delta = left - right;
            return (sum.Magnitude, delta.Magnitude, sum.Unit,
                $"{sum} computed with the overloaded + operator on TelemetryPacket<float>.");
        }

        if (a is TelemetryChannel<bool> actA && b is TelemetryChannel<bool> actB)
        {
            var left = actA.Window().LastOrDefault();
            var right = actB.Window().LastOrDefault();
            var either = left + right;
            var only = left - right;
            return (either.Magnitude, only.Magnitude, "state",
                $"{either} computed with the overloaded + operator on TelemetryPacket<bool> (logical OR).");
        }

        return (0d, 0d, string.Empty, "The two nodes publish different primitives and cannot be aggregated.");
    }

    public TopologyValidationReport ValidateTopology() => new TopologyValidator().Validate(Topology);

    public TopologyValidationReport ValidateTopology(DeploymentNode root) => new TopologyValidator().Validate(root);

    public string? ResolvePath(string macAddress) => TopologyValidator.FindPath(Topology, macAddress);

    public void ReplaceTopology(DeploymentNode root) => Topology = root;

    private void AttachToTopology(SensorRegistration registration)
    {
        var facility = FindOrCreate(Topology, registration.Location.Facility, DeploymentNodeType.Facility);
        var zone = FindOrCreate(facility, registration.Location.Zone, DeploymentNodeType.Zone);

        var parent = zone;
        if (!string.IsNullOrWhiteSpace(registration.Location.SubZone))
        {
            parent = FindOrCreate(zone, registration.Location.SubZone, DeploymentNodeType.SubZone);
        }

        var existing = parent.Children.FirstOrDefault(c =>
            string.Equals(c.MacAddress, registration.MacAddress, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.Name = registration.Location.NodeId;
            existing.Category = registration.Category;
            return;
        }

        parent.AddChild(DeploymentNode.Device(
            registration.Location.NodeId,
            registration.MacAddress,
            registration.Category));
    }

    private static DeploymentNode FindOrCreate(DeploymentNode parent, string name, DeploymentNodeType childType)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Unassigned";
        }

        var existing = parent.Children.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase) &&
            c.NodeType != DeploymentNodeType.Device);

        if (existing is not null)
        {
            return existing;
        }

        return parent.AddChild(DeploymentNode.Container(name, childType, maxDevices: 32, powerBudget: 900d));
    }

    private static DeploymentNode SeedTopologyRoot()
        => DeploymentNode.Container("Smart-X Mesh", DeploymentNodeType.Mesh, maxDevices: 0, powerBudget: 50_000d);
}
