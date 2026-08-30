using System.Threading.Channels;
using CryptoSpot.Application.Abstractions.Services.RealTime;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.BackgroundServices;

public sealed class MatchEngineEventQueue : BackgroundService, IMatchEngineEventQueue
{
    private readonly Channel<Func<IRealTimeDataPushService, CancellationToken, Task>> _channel;
    private readonly IRealTimeDataPushService _pushService;
    private readonly ILogger<MatchEngineEventQueue> _logger;

    public MatchEngineEventQueue(
        IRealTimeDataPushService pushService,
        ILogger<MatchEngineEventQueue> logger)
    {
        _pushService = pushService;
        _logger = logger;
        _channel = Channel.CreateBounded<Func<IRealTimeDataPushService, CancellationToken, Task>>(
            new BoundedChannelOptions(50_000)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
    }

    public ValueTask EnqueueAsync(
        Func<IRealTimeDataPushService, CancellationToken, Task> pushWork,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pushWork);
        return _channel.Writer.WriteAsync(pushWork, cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var pushWork in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await pushWork(_pushService, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process match engine push event");
            }
        }
    }
}