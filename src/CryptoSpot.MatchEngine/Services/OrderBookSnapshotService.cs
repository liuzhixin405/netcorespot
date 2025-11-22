using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CryptoSpot.Application.Abstractions.Services.RealTime;
using CryptoSpot.Application.DTOs.Trading;
using CryptoSpot.Infrastructure.Repositories.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.MatchEngine.Services
{
    /// <summary>
    /// 订单簿快照推送服务实现
    /// 统一管理订单簿快照的生成和推送逻辑，消除重复代码
    /// </summary>
    public class OrderBookSnapshotService : IOrderBookSnapshotService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<OrderBookSnapshotService> _logger;

        public OrderBookSnapshotService(
            IServiceProvider serviceProvider,
            ILogger<OrderBookSnapshotService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task PushSnapshotAsync(string symbol, int depth = 20, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                _logger.LogWarning("Symbol is null or empty, skipping snapshot push");
                return;
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                
                var redisOrders = scope.ServiceProvider.GetRequiredService<RedisOrderRepository>();
                var realTimePush = scope.ServiceProvider.GetService<IRealTimeDataPushService>();

                if (realTimePush == null)
                {
                    _logger.LogDebug("RealTimeDataPushService not available, skipping snapshot push");
                    return;
                }

                // 获取订单簿深度
                var (bids, asks) = await redisOrders.GetOrderBookDepthAsync(symbol, depth);

                // 转换为 DTO
                var bidDtos = bids.ConvertAll(x => new OrderBookLevelDto
                {
                    Price = x.price,
                    Quantity = x.quantity
                });

                var askDtos = asks.ConvertAll(x => new OrderBookLevelDto
                {
                    Price = x.price,
                    Quantity = x.quantity
                });

                // 推送快照
                await realTimePush.PushExternalOrderBookSnapshotAsync(
                    symbol,
                    bidDtos,
                    askDtos,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

                _logger.LogDebug("Successfully pushed order book snapshot for {Symbol} (bids: {BidsCount}, asks: {AsksCount})",
                    symbol, bidDtos.Count, askDtos.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to push order book snapshot for {Symbol}", symbol);
            }
        }
    }
}
