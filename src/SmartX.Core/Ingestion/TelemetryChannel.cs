using SmartX.Core.Arrays;
using SmartX.Core.Collections;
using SmartX.Core.Domain;
using SmartX.Core.Telemetry;

namespace SmartX.Core.Ingestion;

public interface ITelemetryChannel
{
    string MacAddress { get; }

    SensorCategory Category { get; }

    TelemetryValueKind ValueKind { get; }

    long LastSequence { get; }

    long Accepted { get; }

    int WindowCount { get; }

    void Accept(TelemetryEnvelope envelope);

    IReadOnlyList<TelemetryEnvelope> History(int take);

    double LatestMagnitude { get; }

    int ArchiveWindow();

    TelemetryBatchMatrix Matrix { get; }
}

// one pipe per node, fixed to the exact value type that node sends, so nothing needs converting on the way through
public sealed class TelemetryChannel<T> : ITelemetryChannel
    where T : struct, IEquatable<T>
{
    private readonly CircularTelemetryBuffer<TelemetryPacket<T>> _window;
    private readonly List<double> _stagingRow;
    private readonly object _gate = new();
    private long _lastSequence = -1;
    private long _accepted;

    public TelemetryChannel(SensorRegistration registration, int windowSize = 512, int stagingRowSize = 120)
    {
        Registration = registration;
        MacAddress = registration.MacAddress;
        Category = registration.Category;
        _window = new CircularTelemetryBuffer<TelemetryPacket<T>>(windowSize);
        _stagingRow = new List<double>(stagingRowSize);
        StagingRowSize = stagingRowSize;
        Matrix = new TelemetryBatchMatrix(maxBatches: 32, nodeSlots: 1, timeSlots: 96);
    }

    public SensorRegistration Registration { get; }

    public string MacAddress { get; }

    public SensorCategory Category { get; }

    public TelemetryValueKind ValueKind => Category.ValueKind();

    public int StagingRowSize { get; }

    public TelemetryBatchMatrix Matrix { get; }

    public long LastSequence { get { lock (_gate) { return _lastSequence; } } }

    public long Accepted { get { lock (_gate) { return _accepted; } } }

    public int WindowCount => _window.Count;

    public double LatestMagnitude
    {
        get
        {
            var newest = _window.Newest;
            return newest.IsEmpty ? double.NaN : newest.Magnitude;
        }
    }

    public void Accept(TelemetryEnvelope envelope)
    {
        // the single place a flat number turns back into its real type, everything past this line is already typed
        var value = TelemetryOperator<T>.FromMagnitude(envelope.Numeric);

        var packet = new TelemetryPacket<T>(
            envelope.MacAddress,
            envelope.Metric,
            value,
            string.IsNullOrWhiteSpace(envelope.Unit) ? Category.DefaultUnit() : envelope.Unit,
            envelope.Sequence,
            envelope.TimestampUtc);

        _window.Write(packet);

        lock (_gate)
        {
            _lastSequence = envelope.Sequence;
            _accepted++;
            _stagingRow.Add(packet.Magnitude);

            if (_stagingRow.Count >= StagingRowSize)
            {
                Matrix.AppendBatch(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_stagingRow));
                _stagingRow.Clear();
            }
        }
    }

    public IReadOnlyList<TelemetryEnvelope> History(int take)
    {
        var packets = _window.SnapshotNewest(take);
        var result = new List<TelemetryEnvelope>(packets.Length);

        foreach (var packet in packets)
        {
            result.Add(packet.ToEnvelope(Category));
        }

        return result;
    }

    public TelemetryPacket<T> AggregateWindow()
    {
        var total = default(TelemetryPacket<T>);

        foreach (var packet in _window.Snapshot())
        {
            total = total + packet;
        }

        return total;
    }

    public TelemetryPacket<T> WindowDelta()
    {
        var snapshot = _window.Snapshot();
        if (snapshot.Length < 2) return default;

        return snapshot[^1] - snapshot[0];
    }

    public int ArchiveWindow()
    {
        lock (_gate)
        {
            if (_stagingRow.Count > 0)
            {
                Matrix.AppendBatch(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_stagingRow));
                _stagingRow.Clear();
            }
        }

        var promoted = Matrix.PromoteToCollection(MacAddress, "archive", Category.DefaultUnit());
        return promoted.Count;
    }

    public TelemetryPacket<T>[] Window() => _window.Snapshot();
}
