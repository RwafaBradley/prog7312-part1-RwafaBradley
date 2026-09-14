using SmartX.Core.Domain;
using SmartX.Core.Engagement;
using SmartX.Core.Topology;

namespace SmartX.Api;

public sealed class SensorRegistrationRequest
{
    public string MacAddress { get; set; } = string.Empty;

    public string Alias { get; set; } = string.Empty;

    public SensorCategory Category { get; set; } = SensorCategory.Environmental;

    public string Facility { get; set; } = string.Empty;

    public string Zone { get; set; } = string.Empty;

    public string SubZone { get; set; } = string.Empty;

    public string NodeId { get; set; } = string.Empty;

    public string FirmwareVersion { get; set; } = "esp32-idf-5.2.0";

    public string Unit { get; set; } = string.Empty;

    public double MinExpected { get; set; }

    public double MaxExpected { get; set; } = 100d;

    public SensorRegistration ToDomain() => new()
    {
        MacAddress = MacAddress?.Trim() ?? string.Empty,
        Alias = Alias?.Trim() ?? string.Empty,
        Category = Category,
        FirmwareVersion = FirmwareVersion?.Trim() ?? string.Empty,
        Unit = Unit?.Trim() ?? string.Empty,
        MinExpected = MinExpected,
        MaxExpected = MaxExpected,
        Location = new DeploymentLocation
        {
            Facility = Facility?.Trim() ?? string.Empty,
            Zone = Zone?.Trim() ?? string.Empty,
            SubZone = SubZone?.Trim() ?? string.Empty,
            NodeId = NodeId?.Trim() ?? string.Empty
        }
    };
}

public sealed class SensorView
{
    public string MacAddress { get; set; } = string.Empty;

    public string Alias { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public SensorCategory Category { get; set; }

    public string CategoryName { get; set; } = string.Empty;

    public TelemetryValueKind ValueKind { get; set; }

    public string ValueKindName { get; set; } = string.Empty;

    public string Facility { get; set; } = string.Empty;

    public string Zone { get; set; } = string.Empty;

    public string SubZone { get; set; } = string.Empty;

    public string NodeId { get; set; } = string.Empty;

    public string LocationPath { get; set; } = string.Empty;

    public string FirmwareVersion { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public double MinExpected { get; set; }

    public double MaxExpected { get; set; }

    public DateTimeOffset RegisteredUtc { get; set; }

    public long PacketsAccepted { get; set; }

    public int WindowCount { get; set; }

    public List<AttachmentView> Attachments { get; set; } = new();

    public static SensorView From(SensorRegistration r, long accepted, int windowCount) => new()
    {
        MacAddress = r.MacAddress,
        Alias = r.Alias,
        DisplayName = r.DisplayName,
        Category = r.Category,
        CategoryName = r.Category.ToString(),
        ValueKind = r.ValueKind,
        ValueKindName = r.ValueKind.ToString(),
        Facility = r.Location.Facility,
        Zone = r.Location.Zone,
        SubZone = r.Location.SubZone,
        NodeId = r.Location.NodeId,
        LocationPath = r.Location.Path,
        FirmwareVersion = r.FirmwareVersion,
        Unit = r.Unit,
        MinExpected = r.MinExpected,
        MaxExpected = r.MaxExpected,
        RegisteredUtc = r.RegisteredUtc,
        PacketsAccepted = accepted,
        WindowCount = windowCount,
        Attachments = r.Attachments.ConvertAll(AttachmentView.From)
    };
}

public sealed class AttachmentView
{
    public string Id { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Checksum { get; set; } = string.Empty;

    public DateTimeOffset UploadedUtc { get; set; }

    public string DownloadUrl { get; set; } = string.Empty;

    public static AttachmentView From(AttachmentRecord record) => new()
    {
        Id = record.Id,
        FileName = record.FileName,
        ContentType = record.ContentType,
        SizeBytes = record.SizeBytes,
        Kind = record.Kind,
        Checksum = record.Checksum,
        UploadedUtc = record.UploadedUtc,
        DownloadUrl = $"/api/sensors/{record.MacAddress}/attachments/{record.Id}"
    };
}

public sealed class GatewayStatus
{
    public string Service { get; set; } = "Smart-X Data Ingestion and Validation Gateway";

    public string Version { get; set; } = "1.0.0-part1";

    public string Runtime { get; set; } = string.Empty;

    public DateTimeOffset StartedUtc { get; set; }

    public double UptimeSeconds { get; set; }

    public int RegisteredNodes { get; set; }

    public long PacketsAccepted { get; set; }

    public long PacketsRejected { get; set; }

    public bool SimulatorRunning { get; set; }

    public double MeshIntegrity { get; set; }

    public List<PillarStatus> Pillars { get; set; } = new();
}

public sealed class PillarStatus
{
    public string Key { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public string AvailableIn { get; set; } = string.Empty;
}

public sealed class TopologyNodeView
{
    public string Name { get; set; } = string.Empty;

    public string NodeType { get; set; } = string.Empty;

    public string? MacAddress { get; set; }

    public string? Category { get; set; }

    public List<TopologyNodeView> Children { get; set; } = new();

    public static TopologyNodeView From(DeploymentNode node) => new()
    {
        Name = node.Name,
        NodeType = node.NodeType.ToString(),
        MacAddress = node.MacAddress,
        Category = node.Category?.ToString(),
        Children = node.Children.ConvertAll(From)
    };
}

public sealed class HeartbeatView
{
    public HeartbeatSnapshot Snapshot { get; set; } = new();

    public string StrategyName { get; set; } = "Live Anomaly Heartbeat Wall";
}
