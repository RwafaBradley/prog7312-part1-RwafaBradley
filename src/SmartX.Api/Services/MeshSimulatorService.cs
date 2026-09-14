using System.Text.Json;
using SmartX.Api.Services;

namespace SmartX.Api.Services;

public sealed class MeshSimulatorService : BackgroundService
{
    private readonly GatewayHost _host;
    private readonly ILogger<MeshSimulatorService> _logger;
    private readonly IConfiguration _configuration;

    public MeshSimulatorService(GatewayHost host, ILogger<MeshSimulatorService> logger, IConfiguration configuration)
    {
        _host = host;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMs = _configuration.GetValue("SmartX:TickIntervalMs", 1000);
        var environmental = _configuration.GetValue("SmartX:Seed:Environmental", 9);
        var power = _configuration.GetValue("SmartX:Seed:Power", 5);
        var actuators = _configuration.GetValue("SmartX:Seed:Actuators", 4);

        var seeded = _host.Simulator.SeedMesh(environmental, power, actuators);
        _logger.LogInformation("Seeded {Count} simulated ESP32 nodes into the Smart-X mesh.", seeded.Count);

        // a quiet warm up first, so every node has a normal of its own before anything gets called abnormal
        const int warmUpTicks = 40;
        var origin = DateTimeOffset.UtcNow;

        for (var i = 0; i < warmUpTicks && !stoppingToken.IsCancellationRequested; i++)
        {
            _host.Simulator.Tick(
                origin.AddMilliseconds(-(warmUpTicks - i) * (double)intervalMs),
                allowFaults: false);
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                if (_host.SimulatorEnabled)
                {
                    _host.Simulator.Tick();
                }

                // nothing is packaged up when nobody is watching
                if (_host.Broadcaster.SubscriberCount > 0)
                {
                    var snapshot = _host.Gateway.Heartbeat();

                    _host.Broadcaster.Publish(JsonSerializer.Serialize(snapshot, SmartXJson.Options));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Simulation tick failed; the gateway continues serving requests.");
            }
        }
    }
}
