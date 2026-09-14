using System.Collections.Concurrent;
using System.Threading.Channels;
using SmartX.Core.Ingestion;
using SmartX.Core.Simulation;
using SmartX.Core.Storage;

namespace SmartX.Api.Services;

public sealed class GatewayHost
{
    public GatewayHost(IConfiguration configuration, IHostEnvironment environment)
    {
        var window = configuration.GetValue("SmartX:WindowSize", 512);
        var uploadRoot = configuration.GetValue<string>("SmartX:AttachmentRoot")
                         ?? Path.Combine(environment.ContentRootPath, "App_Data", "attachments");

        Gateway = new IngestionGateway(window);
        Simulator = new TelemetrySimulator(Gateway);
        Attachments = new AttachmentStore(uploadRoot);
        Broadcaster = new TelemetryBroadcaster();
    }

    public IngestionGateway Gateway { get; }

    public TelemetrySimulator Simulator { get; }

    public AttachmentStore Attachments { get; }

    public TelemetryBroadcaster Broadcaster { get; }

    public bool SimulatorEnabled { get; set; } = true;
}

// holds whoever is watching, there is nobody to send to yet but the live feed will plug straight into this
public sealed class TelemetryBroadcaster
{
    private readonly ConcurrentDictionary<Guid, Channel<string>> _subscribers = new();

    public int SubscriberCount => _subscribers.Count;

    public (Guid Id, ChannelReader<string> Reader) Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(4)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        _subscribers[id] = channel;
        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    public void Publish(string payload)
    {
        foreach (var subscriber in _subscribers.Values)
        {
            subscriber.Writer.TryWrite(payload);
        }
    }
}
