using SmartX.Core.Domain;

namespace SmartX.Core.Telemetry;

// json cannot describe a generic type, so a reading travels flat and is put back into its real type on arrival
public sealed class TelemetryEnvelope
{
    public string MacAddress { get; set; } = string.Empty;

    public string Metric { get; set; } = string.Empty;

    public SensorCategory Category { get; set; }

    public TelemetryValueKind Kind { get; set; }

    public double Numeric { get; set; }

    public string Unit { get; set; } = string.Empty;

    public long Sequence { get; set; }

    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class IngestionResult
{
    public bool Accepted { get; init; }

    public string MacAddress { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();

    public long AcceptedCount { get; init; }

    public long RejectedCount { get; init; }

    public static IngestionResult Ok(string mac, string message, long accepted, long rejected) => new()
    {
        Accepted = true,
        MacAddress = mac,
        Message = message,
        AcceptedCount = accepted,
        RejectedCount = rejected
    };

    public static IngestionResult Fail(string mac, IReadOnlyList<string> errors, long accepted, long rejected) => new()
    {
        Accepted = false,
        MacAddress = mac,
        Message = "Packet rejected by the validation pipeline.",
        ValidationErrors = errors,
        AcceptedCount = accepted,
        RejectedCount = rejected
    };
}
