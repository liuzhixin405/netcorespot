using CryptoSpot.Application.Abstractions.Services.RealTime;

namespace CryptoSpot.Infrastructure.BackgroundServices;

public interface IMatchEngineEventQueue
{
    ValueTask EnqueueAsync(
        Func<IRealTimeDataPushService, CancellationToken, Task> pushWork,
        CancellationToken cancellationToken = default);
}