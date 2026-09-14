using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartX.Core.Domain;
using SmartX.Core.Engagement;
using SmartX.Core.Telemetry;
using SmartX.Core.Topology;

namespace SmartX.Desktop.Client.Services;

public sealed class GatewayException : Exception
{
    public GatewayException(string message, IReadOnlyList<string>? errors = null) : base(message)
        => Errors = errors ?? Array.Empty<string>();

    public IReadOnlyList<string> Errors { get; }
}

public sealed class PillarStatus
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string AvailableIn { get; set; } = string.Empty;
}

public sealed class GatewayStatus
{
    public string Service { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
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

    public string SizeDisplay => SizeBytes < 1024
        ? $"{SizeBytes} B"
        : SizeBytes < 1024 * 1024
            ? $"{SizeBytes / 1024d:F1} KB"
            : $"{SizeBytes / (1024d * 1024d):F2} MB";
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
}

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
}

public sealed class HeartbeatView
{
    public HeartbeatSnapshot Snapshot { get; set; } = new();
    public string StrategyName { get; set; } = string.Empty;
}

public sealed class AggregateResult
{
    public string NodeA { get; set; } = string.Empty;
    public string NodeB { get; set; } = string.Empty;
    public double Aggregate { get; set; }
    public double Delta { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

public sealed class AttachmentUploadResult
{
    public List<AttachmentView> Attached { get; set; } = new();
    public List<string> Rejected { get; set; } = new();
}

public sealed class ResolvedPath
{
    public string Mac { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int DevicesInTree { get; set; }
}

public sealed class TopologyNodeView
{
    public string Name { get; set; } = string.Empty;
    public string NodeType { get; set; } = string.Empty;
    public string? MacAddress { get; set; }
    public string? Category { get; set; }
    public List<TopologyNodeView> Children { get; set; } = new();
}

public sealed class GatewayClient : IDisposable
{
    // one client for the life of the app, a fresh one per call leaves sockets behind until the machine runs out of them
    private readonly HttpClient _http;

    public static readonly JsonSerializerOptions Json = BuildJson();

    public GatewayClient(string baseAddress = "http://localhost:5240/")
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseAddress),
            Timeout = TimeSpan.FromSeconds(30)
        };

        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public Uri BaseAddress => _http.BaseAddress!;

    private static JsonSerializerOptions BuildJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null));
        return options;
    }

    public Task<GatewayStatus?> GetStatusAsync(CancellationToken ct = default)
        => GetAsync<GatewayStatus>("api/status", ct);

    public Task<bool> PingAsync(CancellationToken ct = default) => TryPingAsync(ct);

    private async Task<bool> TryPingAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync("api/health", ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<SensorView>> GetSensorsAsync(CancellationToken ct = default)
        => await GetAsync<List<SensorView>>("api/sensors", ct).ConfigureAwait(false) ?? new List<SensorView>();

    public Task<SensorView?> GetSensorAsync(string mac, CancellationToken ct = default)
        => GetAsync<SensorView>($"api/sensors/{Uri.EscapeDataString(mac)}", ct);

    public async Task<SensorView> RegisterSensorAsync(SensorRegistrationRequest request, CancellationToken ct = default)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        using var response = await _http
            .PostAsJsonAsync("api/sensors", request, Json, ct)
            .ConfigureAwait(false);

        Record("POST", "api/sensors", (int)response.StatusCode,
            System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        await ThrowIfFailedAsync(response, ct).ConfigureAwait(false);

        return await response.Content.ReadFromJsonAsync<SensorView>(Json, ct).ConfigureAwait(false)
               ?? throw new GatewayException("The gateway accepted the registration but returned no node.");
    }

    public async Task DeleteSensorAsync(string mac, CancellationToken ct = default)
    {
        using var response = await _http
            .DeleteAsync($"api/sensors/{Uri.EscapeDataString(mac)}", ct)
            .ConfigureAwait(false);

        await ThrowIfFailedAsync(response, ct).ConfigureAwait(false);
    }

    public async Task<AttachmentUploadResult> UploadAttachmentsAsync(
        string mac,
        IEnumerable<string> filePaths,
        CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var streams = new List<FileStream>();

        try
        {
            foreach (var path in filePaths)
            {
                // the file is streamed rather than read into an array, so a twenty megabyte photo never lands in memory whole
                var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 80 * 1024, useAsync: true);
                streams.Add(stream);

                var content = new StreamContent(stream);
                content.Headers.ContentType = new MediaTypeHeaderValue(GuessContentType(path));
                form.Add(content, "files", Path.GetFileName(path));
            }

            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            using var response = await _http
                .PostAsync($"api/sensors/{Uri.EscapeDataString(mac)}/attachments", form, ct)
                .ConfigureAwait(false);

            Record("POST", "api/sensors/attachments", (int)response.StatusCode,
                System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            await ThrowIfFailedAsync(response, ct).ConfigureAwait(false);

            return await response.Content.ReadFromJsonAsync<AttachmentUploadResult>(Json, ct).ConfigureAwait(false)
                   ?? new AttachmentUploadResult();
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task DeleteAttachmentAsync(string mac, string id, CancellationToken ct = default)
    {
        using var response = await _http
            .DeleteAsync($"api/sensors/{Uri.EscapeDataString(mac)}/attachments/{id}", ct)
            .ConfigureAwait(false);

        await ThrowIfFailedAsync(response, ct).ConfigureAwait(false);
    }

    public async Task DownloadAttachmentAsync(
        string mac, string id, string destinationPath, CancellationToken ct = default)
    {
        using var response = await _http
            .GetAsync($"api/sensors/{Uri.EscapeDataString(mac)}/attachments/{id}",
                HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        await ThrowIfFailedAsync(response, ct).ConfigureAwait(false);

        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize: 80 * 1024, useAsync: true);

        await source.CopyToAsync(destination, ct).ConfigureAwait(false);
    }

    public async Task<IngestionResult> IngestAsync(TelemetryEnvelope envelope, CancellationToken ct = default)
    {
        using var response = await _http
            .PostAsJsonAsync("api/telemetry", envelope, Json, ct)
            .ConfigureAwait(false);

        var result = await response.Content.ReadFromJsonAsync<IngestionResult>(Json, ct).ConfigureAwait(false);

        return result ?? throw new GatewayException("The gateway returned no ingestion result.");
    }

    public async Task<IReadOnlyList<TelemetryEnvelope>> GetHistoryAsync(
        string mac, int take = 60, CancellationToken ct = default)
        => await GetAsync<List<TelemetryEnvelope>>(
               $"api/telemetry/{Uri.EscapeDataString(mac)}?take={take}", ct).ConfigureAwait(false)
           ?? new List<TelemetryEnvelope>();

    public async Task<HeartbeatSnapshot?> GetHeartbeatAsync(CancellationToken ct = default)
        => (await GetAsync<HeartbeatView>("api/heartbeat", ct).ConfigureAwait(false))?.Snapshot;

    // headers only mode matters here, a live feed never ends so waiting for the whole body would wait forever
    public async IAsyncEnumerable<HeartbeatSnapshot> StreamHeartbeatAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/telemetry/stream");
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);

            if (line is null) yield break;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var payload = line[5..].Trim();
            if (payload.Length == 0) continue;

            HeartbeatSnapshot? snapshot;
            try
            {
                snapshot = JsonSerializer.Deserialize<HeartbeatSnapshot>(payload, Json);
            }
            catch (JsonException)
            {
                continue;
            }

            if (snapshot is not null)
            {
                yield return snapshot;
            }
        }
    }

    public Task<AggregateResult?> AggregateAsync(string a, string b, CancellationToken ct = default)
        => GetAsync<AggregateResult>(
            $"api/aggregate?a={Uri.EscapeDataString(a)}&b={Uri.EscapeDataString(b)}", ct);

    public Task<TopologyValidationReport?> ValidateTopologyAsync(CancellationToken ct = default)
        => GetAsync<TopologyValidationReport>("api/topology/validate", ct);

    public Task<TopologyNodeView?> GetTopologyAsync(CancellationToken ct = default)
        => GetAsync<TopologyNodeView>("api/topology", ct);

    public Task<ResolvedPath?> ResolvePathAsync(string mac, CancellationToken ct = default)
        => GetAsync<ResolvedPath>($"api/topology/resolve/{Uri.EscapeDataString(mac)}", ct);

    public async Task InjectFaultAsync(string mac, string kind, CancellationToken ct = default)
    {
        using var response = await _http
            .PostAsync($"api/simulator/fault?mac={Uri.EscapeDataString(mac)}&kind={kind}", null, ct)
            .ConfigureAwait(false);

        await ThrowIfFailedAsync(response, ct).ConfigureAwait(false);
    }

    public async Task<bool> ToggleSimulatorAsync(CancellationToken ct = default)
    {
        using var response = await _http.PostAsync("api/simulator/toggle", null, ct).ConfigureAwait(false);
        await ThrowIfFailedAsync(response, ct).ConfigureAwait(false);

        var payload = await response.Content
            .ReadFromJsonAsync<SimulatorState>(Json, ct)
            .ConfigureAwait(false);

        return payload?.Running ?? false;
    }

    public async Task<int> ArchiveWindowAsync(string mac, CancellationToken ct = default)
    {
        using var response = await _http
            .PostAsync($"api/archive/{Uri.EscapeDataString(mac)}", null, ct)
            .ConfigureAwait(false);

        await ThrowIfFailedAsync(response, ct).ConfigureAwait(false);

        var payload = await response.Content
            .ReadFromJsonAsync<ArchiveResult>(Json, ct)
            .ConfigureAwait(false);

        return payload?.PromotedToList ?? 0;
    }

    private sealed class SimulatorState { public bool Running { get; set; } }

    private sealed class ArchiveResult
    {
        public string Mac { get; set; } = string.Empty;
        public int PromotedToList { get; set; }
    }

    private double _latencyEwma;

    public double AverageLatencyMs => _latencyEwma;

    public string LastCall { get; private set; } = string.Empty;

    public int LastStatusCode { get; private set; }

    private void Record(string method, string path, int statusCode, double elapsedMs)
    {
        LastCall = $"{method} /{path.Split('?')[0]}";
        LastStatusCode = statusCode;
        _latencyEwma = _latencyEwma <= 0d ? elapsedMs : (_latencyEwma * 0.8d) + (elapsedMs * 0.2d);
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        using var response = await _http.GetAsync(path, ct).ConfigureAwait(false);
        Record("GET", path, (int)response.StatusCode,
            System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        await ThrowIfFailedAsync(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false);
    }

    private static async Task ThrowIfFailedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var message = $"{(int)response.StatusCode} {response.ReasonPhrase}";
        var errors = new List<string>();

        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ErrorPayload>(Json, ct).ConfigureAwait(false);

            if (problem is not null)
            {
                if (!string.IsNullOrWhiteSpace(problem.Message)) message = problem.Message;
                else if (!string.IsNullOrWhiteSpace(problem.Title)) message = problem.Title;

                if (problem.Errors is { Count: > 0 }) errors.AddRange(problem.Errors);
                if (problem.ValidationErrors is { Count: > 0 }) errors.AddRange(problem.ValidationErrors);
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
        }

        throw new GatewayException(message, errors);
    }

    private sealed class ErrorPayload
    {
        public string? Message { get; set; }
        public string? Title { get; set; }
        public List<string>? Errors { get; set; }
        public List<string>? ValidationErrors { get; set; }
    }

    private static string GuessContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => "application/json",
        ".xml" => "application/xml",
        ".csv" => "text/csv",
        ".yaml" or ".yml" => "application/yaml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".zip" => "application/zip",
        ".log" or ".txt" or ".ini" or ".cfg" or ".conf" or ".toml" or ".ndjson" => "text/plain",
        _ => "application/octet-stream"
    };

    public void Dispose() => _http.Dispose();
}
