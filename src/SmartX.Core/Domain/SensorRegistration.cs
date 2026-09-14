namespace SmartX.Core.Domain;

public sealed class DeploymentLocation
{
    public string Facility { get; set; } = string.Empty;

    public string Zone { get; set; } = string.Empty;

    public string SubZone { get; set; } = string.Empty;

    public string NodeId { get; set; } = string.Empty;

    public string Path => string.Join(" > ", new[] { Facility, Zone, SubZone, NodeId }
        .Where(segment => !string.IsNullOrWhiteSpace(segment)));

    public override string ToString() => Path;
}

public sealed class AttachmentRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string MacAddress { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/octet-stream";

    public long SizeBytes { get; set; }

    public string Kind { get; set; } = "Unknown";

    public string StoredPath { get; set; } = string.Empty;

    public DateTimeOffset UploadedUtc { get; set; } = DateTimeOffset.UtcNow;

    public string Checksum { get; set; } = string.Empty;
}

public sealed class SensorRegistration
{
    public string MacAddress { get; set; } = string.Empty;

    public string Alias { get; set; } = string.Empty;

    public SensorCategory Category { get; set; } = SensorCategory.Environmental;

    public DeploymentLocation Location { get; set; } = new();

    public string FirmwareVersion { get; set; } = "esp32-idf-5.2.0";

    public string Unit { get; set; } = string.Empty;

    // the sane operating band for this node, nothing is measured against it yet, that comes with the checks
    public double MinExpected { get; set; }

    public double MaxExpected { get; set; } = 100d;

    public DateTimeOffset RegisteredUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<AttachmentRecord> Attachments { get; set; } = new();

    public TelemetryValueKind ValueKind => Category.ValueKind();

    public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? MacAddress : Alias;
}
