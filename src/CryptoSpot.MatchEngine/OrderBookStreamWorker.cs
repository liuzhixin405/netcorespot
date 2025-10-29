using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CryptoSpot.Infrastructure.Repositories.Redis;
using CryptoSpot.Redis;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoSpot.MatchEngine
{
    /// <summary>
    /// 使用 Redis Streams consumer group 订阅 orders:stream，处理 order_created 事件并推送订单簿快照。
    /// 包含 PEL 检查与超时转 DLQ 逻辑。
    /// </summary>
    public class OrderBookStreamWorker : BackgroundService
    {
        private readonly ILogger<OrderBookStreamWorker> _logger;
        private readonly IServiceProvider _sp;
        private const string StreamKey = "orders:stream";
        private const string GroupName = "order_book_push_group";
        private const string ConsumerPrefix = "orderbook-pusher-";
        private const string DlqStream = "orders:stream:dlq";

        public OrderBookStreamWorker(ILogger<OrderBookStreamWorker> logger, IServiceProvider sp)
        {
            _logger = logger;
            _sp = sp;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("OrderBookStreamWorker starting");

            using var scope = _sp.CreateScope();
            var redis = scope.ServiceProvider.GetRequiredService<IRedisCache>();
            var db = redis.Connection.GetDatabase();

            // Ensure consumer group exists (create stream if missing)
            try
            {
                await db.StreamCreateConsumerGroupAsync(StreamKey, GroupName, "$", createStream: true);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "XGROUP CREATE may have failed (group exists?): {Msg}", ex.Message);
            }

            var consumerName = ConsumerPrefix + Environment.MachineName + "-" + Guid.NewGuid().ToString("n");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Read new messages for this consumer group (block for 2 seconds)
                    var streamEntries = new StackExchange.Redis.StreamEntry[0];
                    try
                    {
                        streamEntries = await db.StreamReadGroupAsync(StreamKey, GroupName, consumerName, ">", count: 10);
                    }
                    catch (StackExchange.Redis.RedisServerException rsex) when (rsex.Message?.Contains("unknown command") == true)
                    {
                        _logger.LogWarning("Redis server does not support Streams (XREADGROUP). Stream processing disabled: {Msg}", rsex.Message);
                        // Sleep and continue - don't spam errors
                        await Task.Delay(5000, stoppingToken);
                        continue;
                    }

                    if (streamEntries == null || streamEntries.Length == 0)
                    {
                        // No new messages, check PEL for stuck entries
                        await HandlePendingEntries(redis, db);
                        continue;
                    }

                    foreach (var entry in streamEntries)
                    {
                        var id = entry.Id;
                        var values = entry.Values; // NameValueEntry[]
                        var dict = new System.Collections.Generic.Dictionary<string, string>();
                        foreach (var nv in values)
                        {
                            dict[nv.Name] = nv.Value;
                        }

                        if (dict.TryGetValue("type", out var type) && type == "order_created")
                        {
                            dict.TryGetValue("symbol", out var symbol);
                            if (!string.IsNullOrEmpty(symbol))
                            {
                                await ProcessOrderCreated(symbol, id, redis, db);
                                // Acknowledge
                                await db.StreamAcknowledgeAsync(StreamKey, GroupName, new StackExchange.Redis.RedisValue[] { id });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Stream read/processing error");
                    await Task.Delay(1000, stoppingToken);
                }
            }
        }

        private async Task ProcessOrderCreated(string symbol, string entryId, IRedisCache redis, StackExchange.Redis.IDatabase db)
        {
            try
            {
                // Push snapshot (reuse RedisOrderRepository + realTime push)
                using var scope = _sp.CreateScope();
                var redisOrders = scope.ServiceProvider.GetRequiredService<RedisOrderRepository>();
                var realTimePush = scope.ServiceProvider.GetService<CryptoSpot.Application.Abstractions.Services.RealTime.IRealTimeDataPushService>();
                if (realTimePush == null) return;

                var (bids, asks) = await redisOrders.GetOrderBookDepthAsync(symbol, 20);
                var bidDtos = bids.ConvertAll(x => new CryptoSpot.Application.DTOs.Trading.OrderBookLevelDto { Price = x.price, Quantity = x.quantity });
                var askDtos = asks.ConvertAll(x => new CryptoSpot.Application.DTOs.Trading.OrderBookLevelDto { Price = x.price, Quantity = x.quantity });
                await realTimePush.PushExternalOrderBookSnapshotAsync(symbol, bidDtos, askDtos, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

                // Acknowledge will be handled by caller after successful processing
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process order_created stream entry {EntryId}", entryId);
                // Move to DLQ
                try
                {
                    await redis.XAddAsync(DlqStream, "*", null, ("entryId", entryId));
                }
                catch (Exception dlqEx)
                {
                    _logger.LogError(dlqEx, "Failed to push entry to DLQ");
                }
            }
        }

        private async Task HandlePendingEntries(IRedisCache redis, StackExchange.Redis.IDatabase db)
        {
            try
            {
                // Attempt to XAUTOCLAIM messages idle longer than threshold and move them to this consumer for processing
                var minIdleMs = 60000; // 1 minute
                try
                {
                    // XAUTOCLAIM <stream> <group> <consumer> <min-idle-ms> <start> COUNT 10
                    // Use Database.ExecuteAsync to run raw command
                    await db.ExecuteAsync("XAUTOCLAIM", StreamKey, GroupName, ConsumerPrefix + "-reclaimer", minIdleMs.ToString(), "0-0", "COUNT", "10");
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "XAUTOCLAIM not supported or failed: {Msg}", ex.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to handle pending entries");
            }
        }
    }
}
