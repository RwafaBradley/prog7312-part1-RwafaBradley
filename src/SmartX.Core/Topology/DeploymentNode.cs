using SmartX.Core.Domain;

namespace SmartX.Core.Topology;

public enum DeploymentNodeType
{
    Mesh = 0,
    Facility = 1,
    Zone = 2,
    SubZone = 3,
    Device = 4
}

// a node holds its own children, so asking about one branch means asking the same question one level down
public sealed class DeploymentNode
{
    public string Name { get; set; } = string.Empty;

    public DeploymentNodeType NodeType { get; set; } = DeploymentNodeType.Mesh;

    public string? MacAddress { get; set; }

    public SensorCategory? Category { get; set; }

    public int MaxDevices { get; set; } = 32;

    public double PowerBudgetWatts { get; set; } = 500d;

    public List<DeploymentNode> Children { get; set; } = new();

    public bool IsLeaf => Children.Count == 0;

    public DeploymentNode AddChild(DeploymentNode child)
    {
        Children.Add(child);
        return child;
    }

    public static DeploymentNode Container(string name, DeploymentNodeType type, int maxDevices = 32, double powerBudget = 500d)
        => new() { Name = name, NodeType = type, MaxDevices = maxDevices, PowerBudgetWatts = powerBudget };

    public static DeploymentNode Device(string name, string macAddress, SensorCategory category)
        => new() { Name = name, NodeType = DeploymentNodeType.Device, MacAddress = macAddress, Category = category };
}

public enum FindingSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}

public sealed class ValidationFinding
{
    public string Path { get; set; } = string.Empty;

    public int Depth { get; set; }

    public FindingSeverity Severity { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public override string ToString() => $"{Severity} [{Code}] {Path}: {Message}";
}

public sealed class TopologyValidationReport
{
    public bool IsValid => Findings.All(f => f.Severity != FindingSeverity.Error);

    public List<ValidationFinding> Findings { get; set; } = new();

    public int NodesVisited { get; set; }

    public int DevicesFound { get; set; }

    public int MaxDepthReached { get; set; }

    public double TotalPowerDrawWatts { get; set; }

    public Dictionary<string, string> DevicePaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
