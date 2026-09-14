using SmartX.Core.Domain;

namespace SmartX.Core.Telemetry;

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

    public double Magnitude => TelemetryOperator<T>.ToMagnitude(Value);

    public bool IsEmpty => string.IsNullOrEmpty(MacAddress);

    // two packets now add like numbers, so two meters on the same board read as one total
    public static TelemetryPacket<T> operator +(TelemetryPacket<T> left, TelemetryPacket<T> right)
        => Combine(left, right, TelemetryOperator<T>.Add, "aggregate");

    public static TelemetryPacket<T> operator -(TelemetryPacket<T> left, TelemetryPacket<T> right)
        => Combine(left, right, TelemetryOperator<T>.Subtract, "delta");

    public static bool operator ==(TelemetryPacket<T> left, TelemetryPacket<T> right) => left.Equals(right);

    public static bool operator !=(TelemetryPacket<T> left, TelemetryPacket<T> right) => !left.Equals(right);

    // ordering is by time and not by value, because what matters here is spotting a packet that arrived late
    public static bool operator >(TelemetryPacket<T> left, TelemetryPacket<T> right) => left.CompareTo(right) > 0;

    public static bool operator <(TelemetryPacket<T> left, TelemetryPacket<T> right) => left.CompareTo(right) < 0;

    public static bool operator >=(TelemetryPacket<T> left, TelemetryPacket<T> right) => left.CompareTo(right) >= 0;

    public static bool operator <=(TelemetryPacket<T> left, TelemetryPacket<T> right) => left.CompareTo(right) <= 0;

    // a total is only as fresh as its slowest contributor, so the later timestamp is the one that survives
    private static TelemetryPacket<T> Combine(
        TelemetryPacket<T> left,
        TelemetryPacket<T> right,
        Func<T, T, T> op,
        string metricSuffix)
    {
        if (left.IsEmpty) return right;
        if (right.IsEmpty) return left;

        var metric = string.Equals(left.Metric, right.Metric, StringComparison.Ordinal)
            ? left.Metric
            : $"{left.Metric}+{right.Metric}";

        var mac = string.Equals(left.MacAddress, right.MacAddress, StringComparison.OrdinalIgnoreCase)
            ? left.MacAddress
            : $"{left.MacAddress}|{right.MacAddress}";

        return new TelemetryPacket<T>(
            mac,
            $"{metric}:{metricSuffix}",
            op(left.Value, right.Value),
            left.Unit,
            Math.Max(left.Sequence, right.Sequence),
            left.TimestampUtc >= right.TimestampUtc ? left.TimestampUtc : right.TimestampUtc);
    }

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
