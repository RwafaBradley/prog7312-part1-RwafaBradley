using System.Text.Json;
using SmartX.Api;
using SmartX.Api.Services;
using SmartX.Core.Domain;
using SmartX.Core.Telemetry;
using SmartX.Core.Topology;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<GatewayHost>();
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
