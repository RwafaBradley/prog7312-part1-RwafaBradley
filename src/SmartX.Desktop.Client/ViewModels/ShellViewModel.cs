using System.Collections.ObjectModel;
using SmartX.Core.Domain;
using SmartX.Core.Engagement;
using SmartX.Core.Telemetry;
using SmartX.Core.Topology;
using SmartX.Desktop.Client.Mvvm;
using SmartX.Desktop.Client.Services;

namespace SmartX.Desktop.Client.ViewModels;

public enum Section
{
    Gateway = 0,
    Overview = 1,
    SignalRadar = 2,
    RegisterSensor = 3,
    TelemetryStream = 4,
    DeploymentTree = 5,
    FileAttachments = 6
}

public sealed class PillarViewModel : ObservableObject
{
    public PillarViewModel(string key, string title, string summary, bool enabled, string availableIn, string index)
    {
        Key = key;
        Title = title;
        Summary = summary;
        IsEnabled = enabled;
        AvailableIn = availableIn;
        Index = index;
    }

    public string Key { get; }
    public string Title { get; }
    public string Summary { get; }
    public bool IsEnabled { get; }
    public string AvailableIn { get; }
    public string Index { get; }
    public string Badge => IsEnabled ? "OPEN" : "LOCKED";
    public string BadgeDetail => IsEnabled ? "available now" : $"arrives in {AvailableIn}";
}

public sealed class ShellViewModel : ObservableObject, IDisposable
{
    private readonly GatewayClient _client;
    private readonly IUiDispatcher _ui;
    private readonly IFileDialogService _files;
    private readonly CancellationTokenSource _lifetime = new();

    private Section _section = Section.Gateway;
    private bool _isConnected;
    private string _connectionMessage = "connecting";
    private string? _statusMessage;
    private bool _statusIsError;
    private string _runtime = string.Empty;
    private string _lastApiCall = string.Empty;
    private int _lastApiStatus;
    private double _latencyMs;

    private int _totalNodes;
    private int _nodesAddedToday;
    private long _totalAccepted;
    private long _totalRejected;
    private double _meshBpm;
    private double _meshIntegrity = 1d;
    private int _healthy, _drifting, _flaring, _stalled, _flatlined;

    private SensorTileViewModel? _selectedTile;
    private SensorView? _selectedSensor;
    private string? _selectedPath;
    private SensorView? _nodeA;
    private SensorView? _nodeB;
    private string? _aggregateResult;
    private string? _archiveResult;
    private TopologyValidationReport? _topologyReport;

    public ShellViewModel(GatewayClient client, IUiDispatcher ui, IFileDialogService files)
    {
        _client = client;
        _ui = ui;
        _files = files;

        Pillars = new ObservableCollection<PillarViewModel>
        {
            new("ingestion", "Sensor Data Ingestion and Telemetry",
                "Register mesh nodes, attach configuration and hardware logs, and watch validated telemetry land in real time.",
                true, "Part 1", "01"),
            new("command", "Real Time Command Stream and History",
                "Issue manual overrides to devices and replay the command history. Queues, stacks and priority queues land here.",
                false, "Part 2", "02"),
            new("topology", "Network Topology and Mesh Routing",
                "Visualise the mesh, trace packet routes and compute power efficient paths with graphs and spanning trees.",
                false, "the final PoE", "03")
        };

        FloatSample = new TypedSampleViewModel("float", "Environmental", "Environmental");
        IntSample = new TypedSampleViewModel("int", "Power", "PowerConsumption");
        BoolSample = new TypedSampleViewModel("bool", "Actuator", "Actuator");

        NavigateCommand = new RelayCommand(parameter =>
        {
            if (parameter is Section section)
            {
                CurrentSection = section;
            }
            else if (parameter is string name && Enum.TryParse<Section>(name, out var parsed))
            {
                CurrentSection = parsed;
            }
        });

        OpenPillarCommand = new RelayCommand(parameter =>
        {
            if (parameter is PillarViewModel { IsEnabled: true })
            {
                CurrentSection = Section.Overview;
            }
        });

        BackToGatewayCommand = new RelayCommand(() => CurrentSection = Section.Gateway);

        RegisterCommand = Guard(new AsyncRelayCommand(RegisterAsync, () => Form.IsValid));
        RefreshCommand = Guard(new AsyncRelayCommand(RefreshSensorsAsync));
        DeregisterCommand = Guard(new AsyncRelayCommand(DeregisterAsync, () => SelectedSensor is not null));
        AttachFilesCommand = Guard(new AsyncRelayCommand(AttachFilesAsync, () => SelectedSensor is not null));
        DownloadAttachmentCommand = Guard(new AsyncRelayCommand(DownloadAttachmentAsync));
        RemoveAttachmentCommand = Guard(new AsyncRelayCommand(RemoveAttachmentAsync));
        InjectFaultCommand = Guard(new AsyncRelayCommand(InjectFaultAsync, _ => SelectedSensor is not null));
        ArchiveWindowCommand = Guard(new AsyncRelayCommand(ArchiveWindowAsync, () => SelectedSensor is not null));
        AggregateCommand = Guard(new AsyncRelayCommand(AggregateAsync, () => NodeA is not null && NodeB is not null));
        ValidateTopologyCommand = Guard(new AsyncRelayCommand(ValidateTopologyAsync));
        ToggleSimulatorCommand = Guard(new AsyncRelayCommand(ToggleSimulatorAsync));

        Form.PropertyChanged += (_, _) => RegisterCommand.RaiseCanExecuteChanged();
    }

    public ObservableCollection<PillarViewModel> Pillars { get; }

    public ObservableCollection<SensorTileViewModel> Tiles { get; } = new();

    public ObservableCollection<RadarPoint> RadarPoints { get; } = new();

    public ObservableCollection<AlertViewModel> Alerts { get; } = new();

    public ObservableCollection<SensorView> Sensors { get; } = new();

    public ObservableCollection<SensorView> RecentRegistrations { get; } = new();

    public ObservableCollection<TelemetryEnvelope> History { get; } = new();

    public ObservableCollection<DeploymentRowViewModel> TreeRows { get; } = new();

    public ObservableCollection<TraceStepViewModel> ValidatorTrace { get; } = new();

    public RegistrationFormViewModel Form { get; } = new();

    public TypedSampleViewModel FloatSample { get; }

    public TypedSampleViewModel IntSample { get; }

    public TypedSampleViewModel BoolSample { get; }

    // every screen watches this one value to work out whether it is the one being shown
    public Section CurrentSection
    {
        get => _section;
        set
        {
            if (!Set(ref _section, value)) return;

            Raise(nameof(IsGateway));
            Raise(nameof(IsOverview));
            Raise(nameof(IsSignalRadar));
            Raise(nameof(IsRegisterSensor));
            Raise(nameof(IsTelemetryStream));
            Raise(nameof(IsDeploymentTree));
            Raise(nameof(IsFileAttachments));
            Raise(nameof(IsInsideIngestion));
            Raise(nameof(SectionTitle));
            Raise(nameof(SectionSubtitle));

            if (value == Section.DeploymentTree)
            {
                _ = ValidateTopologyAsync();
            }
        }
    }

    public bool IsGateway => CurrentSection == Section.Gateway;
    public bool IsOverview => CurrentSection == Section.Overview;
    public bool IsSignalRadar => CurrentSection == Section.SignalRadar;
    public bool IsRegisterSensor => CurrentSection == Section.RegisterSensor;
    public bool IsTelemetryStream => CurrentSection == Section.TelemetryStream;
    public bool IsDeploymentTree => CurrentSection == Section.DeploymentTree;
    public bool IsFileAttachments => CurrentSection == Section.FileAttachments;
    public bool IsInsideIngestion => CurrentSection != Section.Gateway;

    public string SectionTitle => CurrentSection switch
    {
        Section.Overview => "Overview",
        Section.SignalRadar => "Signal Radar",
        Section.RegisterSensor => "Register Sensor",
        Section.TelemetryStream => "Telemetry Stream",
        Section.DeploymentTree => "Deployment Tree",
        Section.FileAttachments => "File Attachments",
        _ => "Smart X Gateway"
    };

    public string SectionSubtitle => CurrentSection switch
    {
        Section.Overview => "live ingestion snapshot across the mesh",
        Section.SignalRadar => "per zone anomaly sweep across all nodes",
        Section.RegisterSensor => "attach a new sensor registration record",
        Section.TelemetryStream => "typed packets, generics, aggregation",
        Section.DeploymentTree => "recursive parent chain validation",
        Section.FileAttachments => "multipart uploads to sensor profiles",
        _ => "choose an architectural pillar"
    };

    public bool IsConnected { get => _isConnected; private set => Set(ref _isConnected, value); }

    public string ConnectionMessage { get => _connectionMessage; private set => Set(ref _connectionMessage, value); }

    public string? StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

    public bool StatusIsError { get => _statusIsError; private set => Set(ref _statusIsError, value); }

    public string Runtime { get => _runtime; private set => Set(ref _runtime, value); }

    public string ApiEndpointDisplay => $".NET 10 API  ·  :{_client.BaseAddress.Port}";

    public string LastApiCall { get => _lastApiCall; private set => Set(ref _lastApiCall, value); }

    public int LastApiStatus
    {
        get => _lastApiStatus;
        private set { if (Set(ref _lastApiStatus, value)) Raise(nameof(LastApiDisplay)); }
    }

    public string LastApiDisplay => string.IsNullOrEmpty(LastApiCall) ? "idle" : $"{LastApiCall}  ·  {LastApiStatus}";

    public int TotalNodes { get => _totalNodes; private set => Set(ref _totalNodes, value); }

    public int NodesAddedToday { get => _nodesAddedToday; private set => Set(ref _nodesAddedToday, value); }

    public long TotalAccepted
    {
        get => _totalAccepted;
        private set { if (Set(ref _totalAccepted, value)) Raise(nameof(PacketsIngestedDisplay)); }
    }

    public string PacketsIngestedDisplay => TotalAccepted >= 1_000_000
        ? $"{TotalAccepted / 1_000_000d:F2}M"
        : TotalAccepted >= 1_000
            ? $"{TotalAccepted / 1_000d:F1}K"
            : TotalAccepted.ToString("N0");

    public long TotalRejected { get => _totalRejected; private set => Set(ref _totalRejected, value); }

    public double MeshBpm
    {
        get => _meshBpm;
        private set { if (Set(ref _meshBpm, value)) Raise(nameof(IngestRateDisplay)); }
    }

    public string IngestRateDisplay => $"{MeshBpm * 60d / 1000d:F1}K / hr";

    public double MeshIntegrity
    {
        get => _meshIntegrity;
        private set { if (Set(ref _meshIntegrity, value)) Raise(nameof(MeshIntegrityDisplay)); }
    }

    public string MeshIntegrityDisplay => $"{Math.Round(MeshIntegrity * 100)}%";

    public double LatencyMs
    {
        get => _latencyMs;
        private set { if (Set(ref _latencyMs, value)) Raise(nameof(LatencyDisplay)); }
    }

    public string LatencyDisplay => $"{Math.Round(LatencyMs)}ms";

    public int Healthy { get => _healthy; private set => Set(ref _healthy, value); }
    public int Drifting { get => _drifting; private set => Set(ref _drifting, value); }
    public int Flaring { get => _flaring; private set => Set(ref _flaring, value); }
    public int Stalled { get => _stalled; private set => Set(ref _stalled, value); }
    public int Flatlined { get => _flatlined; private set => Set(ref _flatlined, value); }

    public int AnomaliesFlagged => Flaring + Stalled + Flatlined;

    public int UnresolvedAnomalies => Flaring + Flatlined;

    public SensorTileViewModel? SelectedTile
    {
        get => _selectedTile;
        set
        {
            if (!Set(ref _selectedTile, value)) return;

            if (value is not null)
            {
                SelectedSensor = Sensors.FirstOrDefault(s =>
                    string.Equals(s.MacAddress, value.MacAddress, StringComparison.OrdinalIgnoreCase));
                _ = LoadSelectionDetailAsync(value.MacAddress);
            }
        }
    }

    public SensorView? SelectedSensor
    {
        get => _selectedSensor;
        set
        {
            if (!Set(ref _selectedSensor, value)) return;

            Raise(nameof(HasSelection));
            Raise(nameof(SelectedSensorDisplay));
            Raise(nameof(SelectedMacAddress));
            DeregisterCommand.RaiseCanExecuteChanged();
            AttachFilesCommand.RaiseCanExecuteChanged();
            InjectFaultCommand.RaiseCanExecuteChanged();
            ArchiveWindowCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasSelection => SelectedSensor is not null;

    public string? SelectedMacAddress
    {
        get => SelectedSensor?.MacAddress;
        set
        {
            if (string.IsNullOrEmpty(value)) return;

            var found = Sensors.FirstOrDefault(s =>
                string.Equals(s.MacAddress, value, StringComparison.OrdinalIgnoreCase));

            if (found is not null && !ReferenceEquals(found, SelectedSensor))
            {
                SelectedSensor = found;
                SelectedTile = Tiles.FirstOrDefault(t =>
                    string.Equals(t.MacAddress, value, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    public string SelectedSensorDisplay => SelectedSensor is null
        ? "no sensor selected"
        : $"{SelectedSensor.MacAddress}  —  {SelectedSensor.CategoryName} · {SelectedSensor.Zone}";

    public string? SelectedPath { get => _selectedPath; private set => Set(ref _selectedPath, value); }

    public SensorView? NodeA
    {
        get => _nodeA;
        set
        {
            if (!Set(ref _nodeA, value)) return;
            Raise(nameof(MeterADisplay));
            AggregateCommand.RaiseCanExecuteChanged();
        }
    }

    public SensorView? NodeB
    {
        get => _nodeB;
        set
        {
            if (!Set(ref _nodeB, value)) return;
            Raise(nameof(MeterBDisplay));
            AggregateCommand.RaiseCanExecuteChanged();
        }
    }

    public string MeterADisplay => TileFor(NodeA) is { } a ? $"{Math.Round(a.LatestValue)} {a.Unit}" : "—";

    public string MeterBDisplay => TileFor(NodeB) is { } b ? $"{Math.Round(b.LatestValue)} {b.Unit}" : "—";

    public string MeterSumDisplay
    {
        get
        {
            var a = TileFor(NodeA);
            var b = TileFor(NodeB);
            return a is null || b is null ? "—" : $"{Math.Round(a.LatestValue + b.LatestValue)} {a.Unit}";
        }
    }

    public string? AggregateResultText { get => _aggregateResult; private set => Set(ref _aggregateResult, value); }

    public string? ArchiveResultText { get => _archiveResult; private set => Set(ref _archiveResult, value); }

    public TopologyValidationReport? TopologyReport
    {
        get => _topologyReport;
        private set
        {
            if (!Set(ref _topologyReport, value)) return;
            Raise(nameof(TopologyFindings));
            Raise(nameof(TopologySummary));
        }
    }

    public IReadOnlyList<ValidationFinding> TopologyFindings
        => TopologyReport?.Findings ?? (IReadOnlyList<ValidationFinding>)Array.Empty<ValidationFinding>();

    public string TopologySummary => TopologyReport is null
        ? "not run yet"
        : $"{TopologyReport.NodesVisited} nodes walked  ·  depth {TopologyReport.MaxDepthReached}  ·  {TopologyReport.DevicesFound} devices";

    public RelayCommand NavigateCommand { get; }
    public RelayCommand OpenPillarCommand { get; }
    public RelayCommand BackToGatewayCommand { get; }
    public AsyncRelayCommand RegisterCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand DeregisterCommand { get; }
    public AsyncRelayCommand AttachFilesCommand { get; }
    public AsyncRelayCommand DownloadAttachmentCommand { get; }
    public AsyncRelayCommand RemoveAttachmentCommand { get; }
    public AsyncRelayCommand InjectFaultCommand { get; }
    public AsyncRelayCommand ArchiveWindowCommand { get; }
    public AsyncRelayCommand AggregateCommand { get; }
    public AsyncRelayCommand ValidateTopologyCommand { get; }
    public AsyncRelayCommand ToggleSimulatorCommand { get; }

    private AsyncRelayCommand Guard(AsyncRelayCommand command)
    {
        command.OnError = Report;
        return command;
    }

    private SensorTileViewModel? TileFor(SensorView? sensor) => sensor is null
        ? null
        : Tiles.FirstOrDefault(t => string.Equals(t.MacAddress, sensor.MacAddress, StringComparison.OrdinalIgnoreCase));

    public void Start()
    {
        _ = RunHeartbeatLoopAsync(_lifetime.Token);
        _ = RunRegistryLoopAsync(_lifetime.Token);
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await foreach (var snapshot in _client.StreamHeartbeatAsync(ct).ConfigureAwait(false))
                {
                    _ui.Invoke(() =>
                    {
                        SetConnected(true, $"streaming from {_client.BaseAddress}");
                        ApplySnapshot(snapshot);
                    });
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _ui.Invoke(() => SetConnected(false, $"gateway unreachable, retrying  ·  {ex.Message}"));
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunRegistryLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        await SafeRefreshAsync(ct).ConfigureAwait(false);

        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            await SafeRefreshAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task SafeRefreshAsync(CancellationToken ct)
    {
        try
        {
            await RefreshSensorsAsync().ConfigureAwait(false);

            var status = await _client.GetStatusAsync(ct).ConfigureAwait(false);
            var topology = await _client.GetTopologyAsync(ct).ConfigureAwait(false);

            _ui.Invoke(() =>
            {
                if (status is not null) Runtime = status.Runtime;
                LatencyMs = _client.AverageLatencyMs;
                LastApiCall = _client.LastCall;
                LastApiStatus = _client.LastStatusCode;
                if (topology is not null) BuildTreeRows(topology);
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }
    }

    private void SetConnected(bool connected, string message)
    {
        IsConnected = connected;
        ConnectionMessage = message;
    }

    public void ApplySnapshot(HeartbeatSnapshot snapshot)
    {
        TotalNodes = snapshot.TotalNodes;
        MeshBpm = snapshot.MeshBpm;
        TotalAccepted = snapshot.TotalAccepted;
        TotalRejected = snapshot.TotalRejected;
        MeshIntegrity = snapshot.MeshIntegrity;
        Healthy = snapshot.Healthy;
        Drifting = snapshot.Drifting;
        Flaring = snapshot.Flaring;
        Stalled = snapshot.Stalled;
        Flatlined = snapshot.Flatlined;
        Raise(nameof(AnomaliesFlagged));
        Raise(nameof(UnresolvedAnomalies));

        var index = new Dictionary<string, SensorTileViewModel>(Tiles.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var tile in Tiles)
        {
            index[tile.MacAddress] = tile;
        }

        for (var position = 0; position < snapshot.Tiles.Count; position++)
        {
            var vitals = snapshot.Tiles[position];

            if (index.TryGetValue(vitals.MacAddress, out var existing))
            {
                existing.Apply(vitals);
                index.Remove(vitals.MacAddress);

                var current = Tiles.IndexOf(existing);
                if (current != position && position < Tiles.Count)
                {
                    Tiles.Move(current, position);
                }
            }
            else
            {
                var created = new SensorTileViewModel(vitals);
                if (position <= Tiles.Count) Tiles.Insert(position, created);
                else Tiles.Add(created);
            }
        }

        foreach (var departed in index.Values)
        {
            Tiles.Remove(departed);

            if (ReferenceEquals(SelectedTile, departed))
            {
                SelectedTile = null;
                SelectedSensor = null;
            }
        }

        BuildRadar(snapshot);
        BuildAlerts(snapshot);
        UpdateTypedSamples();

        Raise(nameof(MeterADisplay));
        Raise(nameof(MeterBDisplay));
        Raise(nameof(MeterSumDisplay));
    }

    // nodes in the same zone share a ring, so a whole zone going quiet shows up as a gap rather than a line in a list
    private void BuildRadar(HeartbeatSnapshot snapshot)
    {
        var zones = snapshot.Tiles
            .Select(ZoneOf)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(z => z, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var byZone = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var existing = RadarPoints.ToDictionary(p => p.MacAddress, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var vitals in snapshot.Tiles)
        {
            var zone = ZoneOf(vitals);
            var ringIndex = Math.Max(zones.IndexOf(zone), 0);
            var countInZone = snapshot.Tiles.Count(t => string.Equals(ZoneOf(t), zone, StringComparison.OrdinalIgnoreCase));

            byZone.TryGetValue(zone, out var slot);
            byZone[zone] = slot + 1;

            var angle = (2d * Math.PI * slot / Math.Max(countInZone, 1)) + (ringIndex * 0.35d);
            var ring = (ringIndex + 1d) / (zones.Count + 0.6d);

            if (!existing.TryGetValue(vitals.MacAddress, out var point))
            {
                point = new RadarPoint(vitals.MacAddress, vitals.DisplayName, zone);
                RadarPoints.Add(point);
            }

            point.Angle = angle;
            point.Ring = ring;
            point.State = vitals.State;
            point.Amplitude = vitals.Amplitude;
            point.Value = vitals.LatestValue;
            point.Category = vitals.Category;
            seen.Add(vitals.MacAddress);
        }

        for (var i = RadarPoints.Count - 1; i >= 0; i--)
        {
            if (!seen.Contains(RadarPoints[i].MacAddress))
            {
                RadarPoints.RemoveAt(i);
            }
        }
    }

    private static string ZoneOf(SensorVitals vitals)
    {
        var parts = vitals.LocationPath.Split('>', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 3 ? parts[2] : parts.Length >= 2 ? parts[1] : "Unzoned";
    }

    private void BuildAlerts(HeartbeatSnapshot snapshot)
    {
        Alerts.Clear();

        foreach (var vitals in snapshot.Tiles.Where(t => t.State != RhythmState.Healthy).Take(6))
        {
            Alerts.Add(new AlertViewModel(vitals));
        }
    }

    private void UpdateTypedSamples()
    {
        Apply(FloatSample, SensorCategory.Environmental);
        Apply(IntSample, SensorCategory.PowerConsumption);
        Apply(BoolSample, SensorCategory.Actuator);

        void Apply(TypedSampleViewModel sample, SensorCategory category)
        {
            var tile = Tiles.FirstOrDefault(t => t.Category == category);
            if (tile is null) return;

            sample.Display = tile.ValueDisplay;
            sample.Raw = category == SensorCategory.Actuator
                ? (tile.LatestValue >= 0.5d ? "true" : "false")
                : category == SensorCategory.PowerConsumption
                    ? Math.Round(tile.LatestValue).ToString("N0")
                    : tile.LatestValue.ToString("F1");
        }
    }

    private void BuildTreeRows(TopologyNodeView root)
    {
        var rows = new List<DeploymentRowViewModel>();
        Flatten(root, 0, rows);

        TreeRows.Clear();
        foreach (var row in rows.Take(14))
        {
            TreeRows.Add(row);
        }

        static void Flatten(TopologyNodeView node, int depth, List<DeploymentRowViewModel> into)
        {
            var isDevice = string.Equals(node.NodeType, "Device", StringComparison.OrdinalIgnoreCase);
            into.Add(new DeploymentRowViewModel(node.Name, node.NodeType.ToLowerInvariant(), depth, node.MacAddress, isDevice));

            foreach (var child in node.Children)
            {
                Flatten(child, depth + 1, into);
            }
        }
    }

    private void BuildValidatorTrace()
    {
        ValidatorTrace.Clear();

        if (TopologyReport is null) return;

        var devices = TreeRows.Where(r => r.IsDevice).Take(3).ToList();

        ValidatorTrace.Add(new TraceStepViewModel("Validate(root)", "mesh root", 0));

        foreach (var device in devices)
        {
            ValidatorTrace.Add(new TraceStepViewModel(
                $"Validate({device.MacAddress ?? device.Name})",
                $"depth {device.Depth}",
                1));
        }

        ValidatorTrace.Add(new TraceStepViewModel(
            TopologyReport.IsValid ? "returns true" : "returns false",
            $"{TopologyReport.NodesVisited} nodes, deepest {TopologyReport.MaxDepthReached}",
            0));
    }

    private async Task RegisterAsync()
    {
        var created = await _client.RegisterSensorAsync(Form.ToRequest(), _lifetime.Token).ConfigureAwait(true);

        Report($"{created.DisplayName} registered at {created.LocationPath}", isError: false);
        Form.Reset();

        await RefreshSensorsAsync().ConfigureAwait(true);

        _ui.Invoke(() =>
        {
            LastApiCall = _client.LastCall;
            LastApiStatus = _client.LastStatusCode;
            SelectedSensor = Sensors.FirstOrDefault(s =>
                string.Equals(s.MacAddress, created.MacAddress, StringComparison.OrdinalIgnoreCase));
            CurrentSection = Section.Overview;
        });
    }

    public async Task RefreshSensorsAsync()
    {
        var sensors = await _client.GetSensorsAsync(_lifetime.Token).ConfigureAwait(true);

        var selectedMac = SelectedSensor?.MacAddress;
        var nodeAMac = NodeA?.MacAddress;
        var nodeBMac = NodeB?.MacAddress;

        _ui.Invoke(() =>
        {
            Sensors.Clear();
            foreach (var sensor in sensors)
            {
                Sensors.Add(sensor);
            }

            RecentRegistrations.Clear();
            foreach (var sensor in sensors.OrderByDescending(s => s.RegisteredUtc).Take(5))
            {
                RecentRegistrations.Add(sensor);
            }

            NodesAddedToday = sensors.Count(s => s.RegisteredUtc.Date == DateTimeOffset.UtcNow.Date);

            SelectedSensor = Find(selectedMac) ?? Sensors.FirstOrDefault();
            NodeA = Find(nodeAMac)
                    ?? Sensors.FirstOrDefault(s => s.Category == SensorCategory.PowerConsumption)
                    ?? Sensors.FirstOrDefault();
            NodeB = Find(nodeBMac)
                    ?? Sensors.LastOrDefault(s => s.Category == SensorCategory.PowerConsumption)
                    ?? Sensors.Skip(1).FirstOrDefault();

            AggregateCommand.RaiseCanExecuteChanged();
        });

        SensorView? Find(string? mac) => mac is null
            ? null
            : Sensors.FirstOrDefault(s => string.Equals(s.MacAddress, mac, StringComparison.OrdinalIgnoreCase));
    }

    private async Task LoadSelectionDetailAsync(string mac)
    {
        try
        {
            var historyTask = _client.GetHistoryAsync(mac, 40, _lifetime.Token);
            var pathTask = _client.ResolvePathAsync(mac, _lifetime.Token);

            await Task.WhenAll(historyTask, pathTask).ConfigureAwait(true);

            var rows = historyTask.Result.Reverse().ToList();
            var resolved = pathTask.Result;

            _ui.Invoke(() =>
            {
                History.Clear();
                foreach (var row in rows) History.Add(row);
                SelectedPath = resolved?.Path;
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ui.Invoke(() => History.Clear());
        }
    }

    private async Task DeregisterAsync()
    {
        if (SelectedSensor is null) return;

        var mac = SelectedSensor.MacAddress;
        await _client.DeleteSensorAsync(mac, _lifetime.Token).ConfigureAwait(true);

        Report($"{mac} deregistered", isError: false);
        SelectedSensor = null;
        SelectedTile = null;

        await RefreshSensorsAsync().ConfigureAwait(true);
    }

    private async Task AttachFilesAsync()
    {
        if (SelectedSensor is null) return;

        var paths = _files.PickFiles(
            $"Attach configuration, logs or photographs to {SelectedSensor.DisplayName}",
            IFileDialogService.AttachmentFilter);

        if (paths.Count == 0) return;

        await UploadAsync(paths).ConfigureAwait(true);
    }

    public async Task UploadAsync(IReadOnlyList<string> paths)
    {
        if (SelectedSensor is null || paths.Count == 0) return;

        var result = await _client
            .UploadAttachmentsAsync(SelectedSensor.MacAddress, paths, _lifetime.Token)
            .ConfigureAwait(true);

        var message = $"{result.Attached.Count} file(s) attached";
        if (result.Rejected.Count > 0)
        {
            message += "  ·  rejected: " + string.Join("; ", result.Rejected);
        }

        Report(message, isError: result.Rejected.Count > 0);

        _ui.Invoke(() =>
        {
            LastApiCall = _client.LastCall;
            LastApiStatus = _client.LastStatusCode;
        });

        await RefreshSensorsAsync().ConfigureAwait(true);
    }

    private async Task DownloadAttachmentAsync(object? parameter)
    {
        if (SelectedSensor is null || parameter is not AttachmentView attachment) return;

        var destination = _files.PickSaveLocation("Save attachment", attachment.FileName, "All files|*.*");
        if (destination is null) return;

        await _client
            .DownloadAttachmentAsync(SelectedSensor.MacAddress, attachment.Id, destination, _lifetime.Token)
            .ConfigureAwait(true);

        Report($"saved {attachment.FileName}", isError: false);
    }

    private async Task RemoveAttachmentAsync(object? parameter)
    {
        if (SelectedSensor is null || parameter is not AttachmentView attachment) return;

        await _client
            .DeleteAttachmentAsync(SelectedSensor.MacAddress, attachment.Id, _lifetime.Token)
            .ConfigureAwait(true);

        Report($"removed {attachment.FileName}", isError: false);
        await RefreshSensorsAsync().ConfigureAwait(true);
    }

    private async Task InjectFaultAsync(object? parameter)
    {
        if (SelectedSensor is null || parameter is not string kind) return;

        await _client.InjectFaultAsync(SelectedSensor.MacAddress, kind, _lifetime.Token).ConfigureAwait(true);
        Report($"{kind} injected into {SelectedSensor.DisplayName}, watch the radar", isError: false);
    }

    private async Task ArchiveWindowAsync()
    {
        if (SelectedSensor is null) return;

        var promoted = await _client
            .ArchiveWindowAsync(SelectedSensor.MacAddress, _lifetime.Token)
            .ConfigureAwait(true);

        ArchiveResultText = $"{promoted} staged readings promoted from the jagged array into List<TelemetryPacket<double>>";
        Report(ArchiveResultText, isError: false);
    }

    private async Task AggregateAsync()
    {
        if (NodeA is null || NodeB is null) return;

        var result = await _client
            .AggregateAsync(NodeA.MacAddress, NodeB.MacAddress, _lifetime.Token)
            .ConfigureAwait(true);

        AggregateResultText = result is null
            ? "the gateway returned no result"
            : $"{result.Aggregate:F0} {result.Unit}   ·   delta {result.Delta:F0} {result.Unit}";

        Raise(nameof(MeterADisplay));
        Raise(nameof(MeterBDisplay));
        Raise(nameof(MeterSumDisplay));
    }

    private async Task ValidateTopologyAsync()
    {
        var report = await _client.ValidateTopologyAsync(_lifetime.Token).ConfigureAwait(true);

        _ui.Invoke(() =>
        {
            TopologyReport = report;

            foreach (var row in TreeRows)
            {
                row.Checked = report?.IsValid ?? false;
            }

            BuildValidatorTrace();
        });

        if (report is not null)
        {
            Report(
                report.IsValid
                    ? $"tree valid, {report.NodesVisited} nodes walked to depth {report.MaxDepthReached}"
                    : $"tree has {report.Findings.Count(f => f.Severity == FindingSeverity.Error)} error(s)",
                isError: !report.IsValid);
        }
    }

    private async Task ToggleSimulatorAsync()
    {
        var running = await _client.ToggleSimulatorAsync(_lifetime.Token).ConfigureAwait(true);
        Report(running ? "simulated mesh resumed" : "simulated mesh paused", isError: false);
    }

    private void Report(Exception exception)
    {
        var message = exception is GatewayException gateway && gateway.Errors.Count > 0
            ? $"{gateway.Message} {string.Join(" ", gateway.Errors)}"
            : exception.Message;

        Report(message, isError: true);
    }

    private void Report(string message, bool isError)
    {
        _ui.Invoke(() =>
        {
            StatusMessage = message;
            StatusIsError = isError;
        });
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        _client.Dispose();
    }
}
