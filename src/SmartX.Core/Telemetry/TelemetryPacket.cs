using SmartX.Core.Domain;

namespace SmartX.Core.Telemetry;

// one reading, kept in its own real type instead of being wrapped up as a loose object
// a struct rather than a class, so a window of ten thousand readings is one block and not ten thousand little objects
public readonly struct TelemetryPacket<T> :
    IEquatable<TelemetryPacket<T>>,
    IComparable<TelemetryPacket<T>>
    where T : struct, IEquatable<T>
{
    public TelemetryPacket(
        string macAddress,
        string metric,
        T value,
        string unit,
        long sequence,
        DateTimeOffset timestampUtc)
    {
        MacAddress = macAddress;
        Metric = metric;
        Value = value;
        Unit = unit;
        Sequence = sequence;
        TimestampUtc = timestampUtc;
    }

    public string MacAddress { get; }

    public string Metric { get; }

    public T Value { get; }

    public string Unit { get; }

    public long Sequence { get; }

    public DateTimeOffset TimestampUtc { get; }

    // flattens any reading to a plain number so one scoring pass can cope with every category
    public double Magnitude => TelemetryOperator<T>.ToMagnitude(Value);

    public bool IsEmpty => string.IsNullOrEmpty(MacAddress);

    public int CompareTo(TelemetryPacket<T> other)
    {
        var byTime = TimestampUtc.CompareTo(other.TimestampUtc);
        return byTime != 0 ? byTime : Sequence.CompareTo(other.Sequence);
    }

    public bool Equals(TelemetryPacket<T> other)
        => string.Equals(MacAddress, other.MacAddress, StringComparison.OrdinalIgnoreCase)
           && string.Equals(Metric, other.Metric, StringComparison.Ordinal)
           && Value.Equals(other.Value)
           && Sequence == other.Sequence
           && TimestampUtc == other.TimestampUtc;

    public override bool Equals(object? obj) => obj is TelemetryPacket<T> other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(
            MacAddress?.ToUpperInvariant(),
            Metric,
            Value,
            Sequence,
            TimestampUtc);

    public override string ToString()
        => $"[{TimestampUtc:HH:mm:ss.fff}] {MacAddress} {Metric} = {Value} {Unit} (#{Sequence})";

    public TelemetryEnvelope ToEnvelope(SensorCategory category) => new()
    {
        MacAddress = MacAddress,
        Metric = Metric,
        Category = category,
        Kind = category.ValueKind(),
        Numeric = Magnitude,
        Unit = Unit,
        Sequence = Sequence,
        TimestampUtc = TimestampUtc
    };
}
