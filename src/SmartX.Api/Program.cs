using System.Text.Json;
using SmartX.Api;
using SmartX.Api.Services;
using SmartX.Core.Domain;
using SmartX.Core.Telemetry;
using SmartX.Core.Topology;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<GatewayHost>();
builder.Services.AddHostedService<MeshSimulatorService>();
builder.Services.AddProblemDetails();

builder.Services.ConfigureHttpJsonOptions(options => SmartXJson.ApplyTo(options.SerializerOptions));

builder.Services.AddCors(options =>
{
    options.AddPolicy("dashboard", policy => policy
        .WithOrigins("http://localhost:5173", "http://localhost:4173")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 30L * 1024 * 1024;
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors("dashboard");

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", utc = DateTimeOffset.UtcNow }))
   .WithName("Health");

// the front door, everything the console shows on its landing screen is read from here
app.MapGet("/api/status", (GatewayHost host) =>
{
    var gateway = host.Gateway;
    var heartbeat = gateway.Heartbeat();

    return Results.Ok(new GatewayStatus
    {
        Runtime = $".NET {Environment.Version}",
        StartedUtc = gateway.StartedUtc,
        UptimeSeconds = (DateTimeOffset.UtcNow - gateway.StartedUtc).TotalSeconds,
        RegisteredNodes = gateway.Registry.Count,
        PacketsAccepted = gateway.AcceptedCount,
        PacketsRejected = gateway.RejectedCount,
        SimulatorRunning = host.SimulatorEnabled,
        MeshIntegrity = heartbeat.MeshIntegrity,
        Pillars = Pillars()
    });
});

app.MapGet("/api/pillars", () => Results.Ok(Pillars()));

app.MapGet("/api/sensors", (GatewayHost host, string? category, string? search) =>
{
    var gateway = host.Gateway;
    IEnumerable<SensorRegistration> source = gateway.Registry.Snapshot();

    if (!string.IsNullOrWhiteSpace(category) &&
        Enum.TryParse<SensorCategory>(category, ignoreCase: true, out var parsed))
    {
        source = source.Where(s => s.Category == parsed);
    }

    if (!string.IsNullOrWhiteSpace(search))
    {
        source = source.Where(s =>
            s.MacAddress.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            s.Alias.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            s.Location.Path.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    var views = source.Select(s =>
    {
        gateway.TryGetChannel(s.MacAddress, out var channel);
        return SensorView.From(s, channel?.Accepted ?? 0, channel?.WindowCount ?? 0);
    }).ToList();

    return Results.Ok(views);
});

app.MapGet("/api/sensors/{mac}", (GatewayHost host, string mac) =>
{
    if (!host.Gateway.Registry.TryGet(mac, out var registration))
    {
        return Results.NotFound(new { message = $"Node '{mac}' is not registered." });
    }

    host.Gateway.TryGetChannel(mac, out var channel);
    return Results.Ok(SensorView.From(registration, channel?.Accepted ?? 0, channel?.WindowCount ?? 0));
});

// a rejected sign up hands back the exact rules that failed, so a form can point at the right box
app.MapPost("/api/sensors", (GatewayHost host, SensorRegistrationRequest request) =>
{
    var (ok, errors, stored) = host.Gateway.Register(request.ToDomain());

    if (!ok)
    {
        return Results.BadRequest(new { message = "Registration rejected.", errors });
    }

    host.Gateway.TryGetChannel(stored.MacAddress, out var channel);
    var view = SensorView.From(stored, channel?.Accepted ?? 0, channel?.WindowCount ?? 0);

    return Results.Created($"/api/sensors/{stored.MacAddress}", view);
});

app.MapDelete("/api/sensors/{mac}", (GatewayHost host, string mac) =>
    host.Gateway.Remove(mac)
        ? Results.NoContent()
        : Results.NotFound(new { message = $"Node '{mac}' is not registered." }));

app.MapPost("/api/sensors/{mac}/attachments", async (
    GatewayHost host,
    string mac,
    HttpRequest request,
    CancellationToken cancellationToken) =>
{
    if (!host.Gateway.Registry.TryGet(mac, out var registration))
    {
        return Results.NotFound(new { message = $"Node '{mac}' is not registered." });
    }

    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { message = "Send the file as multipart/form-data." });
    }

    // the file is read off the request as it arrives rather than held whole, same reason a copy does not load the file first
    var form = await request.ReadFormAsync(cancellationToken);

    if (form.Files.Count == 0)
    {
        return Results.BadRequest(new { message = "No file was supplied." });
    }

    var saved = new List<AttachmentView>();
    var failures = new List<string>();

    foreach (var file in form.Files)
    {
        try
        {
            await using var stream = file.OpenReadStream();

            var record = await host.Attachments.SaveAsync(
                registration.MacAddress,
                file.FileName,
                file.ContentType,
                stream,
                cancellationToken);

            registration.Attachments.Add(record);
            saved.Add(AttachmentView.From(record));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            failures.Add($"{file.FileName}: {ex.Message}");
        }
    }

    if (saved.Count == 0)
    {
        return Results.BadRequest(new { message = "No attachment could be stored.", errors = failures });
    }

    return Results.Ok(new { attached = saved, rejected = failures });
});

app.MapGet("/api/sensors/{mac}/attachments", (GatewayHost host, string mac) =>
    host.Gateway.Registry.TryGet(mac, out var registration)
        ? Results.Ok(registration.Attachments.ConvertAll(AttachmentView.From))
        : Results.NotFound(new { message = $"Node '{mac}' is not registered." }));

app.MapGet("/api/sensors/{mac}/attachments/{id}", (GatewayHost host, string mac, string id) =>
{
    if (!host.Gateway.Registry.TryGet(mac, out var registration))
    {
        return Results.NotFound(new { message = $"Node '{mac}' is not registered." });
    }

    var record = registration.Attachments.FirstOrDefault(a => a.Id == id);
    if (record is null || !host.Attachments.TryOpen(record, out var stream))
    {
        return Results.NotFound(new { message = "Attachment not found." });
    }

    return Results.File(stream, record.ContentType, record.FileName, enableRangeProcessing: true);
});

app.MapDelete("/api/sensors/{mac}/attachments/{id}", (GatewayHost host, string mac, string id) =>
{
    if (!host.Gateway.Registry.TryGet(mac, out var registration))
    {
        return Results.NotFound(new { message = $"Node '{mac}' is not registered." });
    }

    var record = registration.Attachments.FirstOrDefault(a => a.Id == id);
    if (record is null)
    {
        return Results.NotFound(new { message = "Attachment not found." });
    }

    host.Attachments.Delete(record);
    registration.Attachments.Remove(record);
    return Results.NoContent();
});

// every packet returns the running accepted and rejected counts, so the screen can show the filter doing its job
app.MapPost("/api/telemetry", (GatewayHost host, TelemetryEnvelope envelope) =>
{
    var result = host.Gateway.Ingest(envelope);
    return result.Accepted ? Results.Accepted(value: result) : Results.BadRequest(result);
});

app.MapPost("/api/telemetry/batch", (GatewayHost host, List<TelemetryEnvelope> envelopes) =>
{
    var results = host.Gateway.IngestBatch(envelopes);

    return Results.Ok(new
    {
        submitted = envelopes.Count,
        accepted = results.Count(r => r.Accepted),
        rejected = results.Count(r => !r.Accepted),
        results
    });
});

app.MapGet("/api/telemetry/{mac}", (GatewayHost host, string mac, int take = 120) =>
    Results.Ok(host.Gateway.History(mac, Math.Clamp(take, 1, 512))));

// one frame on request, the same frame is about to be pushed out continuously instead
app.MapGet("/api/heartbeat", (GatewayHost host) =>
    Results.Ok(new HeartbeatView { Snapshot = host.Gateway.Heartbeat() }));

// the console is pushed a frame each tick instead of asking on a timer, so what moves on screen matches what actually arrived
app.MapGet("/api/telemetry/stream", async (
    GatewayHost host,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers["X-Accel-Buffering"] = "no";

    var (id, reader) = host.Broadcaster.Subscribe();

    try
    {
        var initial = JsonSerializer.Serialize(host.Gateway.Heartbeat(), SmartXJson.Options);
        await context.Response.WriteAsync($"event: heartbeat\ndata: {initial}\n\n", cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);

        await foreach (var payload in reader.ReadAllAsync(cancellationToken))
        {
            await context.Response.WriteAsync($"event: heartbeat\ndata: {payload}\n\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);
        }
    }
    catch (OperationCanceledException)
    {
    }
    finally
    {
        host.Broadcaster.Unsubscribe(id);
    }
});

app.Run();

static List<PillarStatus> Pillars() =>
[
    new()
    {
        Key = "ingestion",
        Title = "Sensor Data Ingestion and Telemetry",
        Summary = "Register mesh nodes, attach configuration and hardware logs, and watch validated telemetry land in real time.",
        Enabled = true,
        AvailableIn = "Part 1"
    },
    new()
    {
        Key = "command-stream",
        Title = "Real-Time Command Stream and History",
        Summary = "Issue manual overrides to devices and replay the command history. Queues, stacks and priority queues land here.",
        Enabled = false,
        AvailableIn = "Part 2"
    },
    new()
    {
        Key = "topology",
        Title = "Network Topology and Mesh Routing",
        Summary = "Visualise the mesh, trace packet routes and compute power-efficient paths with graphs and minimum spanning trees.",
        Enabled = false,
        AvailableIn = "Final PoE"
    }
];

public partial class Program;
