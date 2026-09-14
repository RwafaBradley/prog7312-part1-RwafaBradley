using SmartX.Core.Domain;

namespace SmartX.Core.Topology;

public sealed class TopologyValidator
{
    // the seatbelt, a tree that nests forever gets reported instead of taking the whole process down with it
    public const int MaxDepth = 12;

    private static readonly IReadOnlyDictionary<DeploymentNodeType, DeploymentNodeType[]> AllowedChildren =
        new Dictionary<DeploymentNodeType, DeploymentNodeType[]>
        {
            [DeploymentNodeType.Mesh] = new[] { DeploymentNodeType.Facility },
            [DeploymentNodeType.Facility] = new[] { DeploymentNodeType.Zone },
            [DeploymentNodeType.Zone] = new[] { DeploymentNodeType.SubZone, DeploymentNodeType.Device },
            [DeploymentNodeType.SubZone] = new[] { DeploymentNodeType.SubZone, DeploymentNodeType.Device },
            [DeploymentNodeType.Device] = Array.Empty<DeploymentNodeType>()
        };

    public TopologyValidationReport Validate(DeploymentNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var report = new TopologyValidationReport();
        var seenMacs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var branch = new HashSet<DeploymentNode>(ReferenceEqualityComparer.Instance);

        Descend(root, parent: null, path: root.Name, depth: 0, report, seenMacs, branch);
        return report;
    }

    // each call checks exactly one node, then hands the rest of the branch back to itself
    private double Descend(
        DeploymentNode node,
        DeploymentNode? parent,
        string path,
        int depth,
        TopologyValidationReport report,
        HashSet<string> seenMacs,
        HashSet<DeploymentNode> branch)
    {
        report.NodesVisited++;
        report.MaxDepthReached = Math.Max(report.MaxDepthReached, depth);

        if (depth > MaxDepth)
        {
            report.Findings.Add(new ValidationFinding
            {
                Path = path,
                Depth = depth,
                Severity = FindingSeverity.Error,
                Code = "TOPO-DEPTH",
                Message = $"Nesting exceeds the supported depth of {MaxDepth}; the subtree below this point was not walked."
            });
            return 0d;
        }

        // catches a node that has somehow become its own ancestor, like a folder shortcut pointing at its own parent
        if (!branch.Add(node))
        {
            report.Findings.Add(new ValidationFinding
            {
                Path = path,
                Depth = depth,
                Severity = FindingSeverity.Error,
                Code = "TOPO-CYCLE",
                Message = "This node already appears on its own ancestor chain; the configuration contains a cycle."
            });
            return 0d;
        }

        try
        {
            ValidateNodeItself(node, parent, path, depth, report, seenMacs);

            if (node.IsLeaf)
            {
                if (node.NodeType == DeploymentNodeType.Device)
                {
                    report.DevicesFound++;

                    if (!string.IsNullOrWhiteSpace(node.MacAddress))
                    {
                        report.DevicePaths[node.MacAddress] = path;
                    }

                    return EstimatedDraw(node);
                }

                report.Findings.Add(new ValidationFinding
                {
                    Path = path,
                    Depth = depth,
                    Severity = FindingSeverity.Warning,
                    Code = "TOPO-EMPTY",
                    Message = $"{node.NodeType} '{node.Name}' contains no devices and will never report telemetry."
                });

                return 0d;
            }

            var subtreeDraw = 0d;
            var directDevices = 0;

            foreach (var child in node.Children)
            {
                if (child.NodeType == DeploymentNodeType.Device)
                {
                    directDevices++;
                }

                subtreeDraw += Descend(
                    child,
                    node,
                    $"{path} > {child.Name}",
                    depth + 1,
                    report,
                    seenMacs,
                    branch);
            }

            if (directDevices > node.MaxDevices)
            {
                report.Findings.Add(new ValidationFinding
                {
                    Path = path,
                    Depth = depth,
                    Severity = FindingSeverity.Error,
                    Code = "TOPO-FANOUT",
                    Message = $"{directDevices} devices are attached directly to a container rated for {node.MaxDevices}."
                });
            }

            if (subtreeDraw > node.PowerBudgetWatts)
            {
                report.Findings.Add(new ValidationFinding
                {
                    Path = path,
                    Depth = depth,
                    Severity = FindingSeverity.Error,
                    Code = "TOPO-POWER",
                    Message = $"Estimated subtree draw of {subtreeDraw:F0} W exceeds the {node.PowerBudgetWatts:F0} W budget."
                });
            }
            else if (subtreeDraw > node.PowerBudgetWatts * 0.85d)
            {
                report.Findings.Add(new ValidationFinding
                {
                    Path = path,
                    Depth = depth,
                    Severity = FindingSeverity.Warning,
                    Code = "TOPO-POWER-NEAR",
                    Message = $"Estimated subtree draw of {subtreeDraw:F0} W is within 15% of the {node.PowerBudgetWatts:F0} W budget."
                });
            }

            if (depth == 0)
            {
                report.TotalPowerDrawWatts = subtreeDraw;
            }

            return subtreeDraw;
        }
        finally
        {
            branch.Remove(node);
        }
    }

    private static void ValidateNodeItself(
        DeploymentNode node,
        DeploymentNode? parent,
        string path,
        int depth,
        TopologyValidationReport report,
        HashSet<string> seenMacs)
    {
        if (string.IsNullOrWhiteSpace(node.Name))
        {
            report.Findings.Add(new ValidationFinding
            {
                Path = path,
                Depth = depth,
                Severity = FindingSeverity.Error,
                Code = "TOPO-NAME",
                Message = "Every node must carry a non-empty name; routing tables key on it."
            });
        }

        if (parent is not null &&
            AllowedChildren.TryGetValue(parent.NodeType, out var allowed) &&
            !allowed.Contains(node.NodeType))
        {
            report.Findings.Add(new ValidationFinding
            {
                Path = path,
                Depth = depth,
                Severity = FindingSeverity.Error,
                Code = "TOPO-NESTING",
                Message = $"A {node.NodeType} may not sit directly inside a {parent.NodeType}."
            });
        }

        if (node.NodeType != DeploymentNodeType.Device)
        {
            if (node.MacAddress is not null)
            {
                report.Findings.Add(new ValidationFinding
                {
                    Path = path,
                    Depth = depth,
                    Severity = FindingSeverity.Warning,
                    Code = "TOPO-MAC-ON-CONTAINER",
                    Message = "A MAC address on a container node is ignored by the router."
                });
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(node.MacAddress))
        {
            report.Findings.Add(new ValidationFinding
            {
                Path = path,
                Depth = depth,
                Severity = FindingSeverity.Error,
                Code = "TOPO-MAC-MISSING",
                Message = "A device leaf must declare a MAC address."
            });
            return;
        }

        if (!MacAddressRules.IsValid(node.MacAddress))
        {
            report.Findings.Add(new ValidationFinding
            {
                Path = path,
                Depth = depth,
                Severity = FindingSeverity.Error,
                Code = "TOPO-MAC-FORMAT",
                Message = $"'{node.MacAddress}' is not a valid 48-bit MAC address."
            });
        }

        if (!seenMacs.Add(node.MacAddress))
        {
            report.Findings.Add(new ValidationFinding
            {
                Path = path,
                Depth = depth,
                Severity = FindingSeverity.Error,
                Code = "TOPO-MAC-DUPLICATE",
                Message = $"MAC address '{node.MacAddress}' is claimed by more than one node in this tree."
            });
        }

        if (node.Category is null)
        {
            report.Findings.Add(new ValidationFinding
            {
                Path = path,
                Depth = depth,
                Severity = FindingSeverity.Warning,
                Code = "TOPO-CATEGORY",
                Message = "Device has no category; the gateway cannot select an ingestion channel for it."
            });
        }
    }

    private static double EstimatedDraw(DeploymentNode device) => device.Category switch
    {
        SensorCategory.Environmental => 0.6d,
        SensorCategory.PowerConsumption => 1.4d,
        SensorCategory.Actuator => 12.0d,
        _ => 1.0d
    };

    public static string? FindPath(DeploymentNode node, string macAddress, string? prefix = null)
    {
        var path = prefix is null ? node.Name : $"{prefix} > {node.Name}";

        if (node.NodeType == DeploymentNodeType.Device &&
            string.Equals(node.MacAddress, macAddress, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        foreach (var child in node.Children)
        {
            var hit = FindPath(child, macAddress, path);
            if (hit is not null)
            {
                return hit;
            }
        }

        return null;
    }

    public static int CountDevices(DeploymentNode node)
    {
        if (node.NodeType == DeploymentNodeType.Device)
        {
            return 1;
        }

        var total = 0;
        foreach (var child in node.Children)
        {
            total += CountDevices(child);
        }

        return total;
    }
}

public static class MacAddressRules
{
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var parts = value.Split(':', '-');
        if (parts.Length != 6) return false;

        foreach (var part in parts)
        {
            if (part.Length != 2) return false;
            if (!byte.TryParse(part, System.Globalization.NumberStyles.HexNumber, null, out _)) return false;
        }

        return true;
    }

    public static string Normalise(string value)
        => value.Replace('-', ':').ToUpperInvariant();
}
