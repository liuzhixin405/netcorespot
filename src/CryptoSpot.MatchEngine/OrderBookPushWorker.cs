using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CryptoSpot.Application.Abstractions.Services.RealTime;
using CryptoSpot.Infrastructure.Repositories.Redis;
using CryptoSpot.Redis;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoSpot.MatchEngine
{
    /// <summary>
    /// 订阅 Redis pub/sub 的 order events，并触发订单簿快照推送
    /// </summary>
    public class OrderBookPushWorker : BackgroundService
    {
        private readonly ILogger<OrderBookPushWorker> _logger;
        private readonly IServiceProvider _sp;

        public OrderBookPushWorker(ILogger<OrderBookPushWorker> logger, IServiceProvider sp)
        {
            _logger = logger;
            _sp = sp;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("OrderBookPushWorker starting");

            using var scope = _sp.CreateScope();
            var redis = scope.ServiceProvider.GetRequiredService<IRedisCache>();
            var sub = redis.Connection.GetSubscriber();

            await sub.SubscribeAsync("orders:events", async (channel, value) =>
            {
                try
                {
                    var json = value.ToString();
                    var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("symbol", out var symbolElem))
                    {
                        var symbol = symbolElem.GetString();
                        if (!string.IsNullOrEmpty(symbol))
                        {
                            await PushSnapshot(symbol);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to handle orders:events message");
                }
            });

            // Keep running until cancellation
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        private async Task PushSnapshot(string symbol)
        {
            try
            {
                using var scope = _sp.CreateScope();
                var redisOrders = scope.ServiceProvider.GetRequiredService<RedisOrderRepository>();
                var realTimePush = scope.ServiceProvider.GetService<IRealTimeDataPushService>();

                if (realTimePush == null)
                {
                    _logger.LogDebug("RealTimeDataPushService not registered, skipping snapshot push");
                    return;
                }

                var (bids, asks) = await redisOrders.GetOrderBookDepthAsync(symbol, 20);
                var bidDtos = bids.ConvertAll(x => new CryptoSpot.Application.DTOs.Trading.OrderBookLevelDto { Price = x.price, Quantity = x.quantity });
                var askDtos = asks.ConvertAll(x => new CryptoSpot.Application.DTOs.Trading.OrderBookLevelDto { Price = x.price, Quantity = x.quantity });

                await realTimePush.PushExternalOrderBookSnapshotAsync(symbol, bidDtos, askDtos, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                _logger.LogDebug("Pushed orderbook snapshot for {Symbol}", symbol);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to push snapshot for {Symbol}", symbol);
            }
        }
    }
}
