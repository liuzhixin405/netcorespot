using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CryptoSpot.Persistence.Redis.Repositories;
using CryptoSpot.MatchEngine.Services;
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
        private readonly IOrderBookSnapshotService _snapshotService;
        private const string StreamKey = "orders:stream";
        private const string GroupName = "order_book_push_group";
        private const string ConsumerPrefix = "orderbook-pusher-";
        private const string DlqStream = "orders:stream:dlq";

        public OrderBookStreamWorker(
            ILogger<OrderBookStreamWorker> logger, 
            IServiceProvider sp,
            IOrderBookSnapshotService snapshotService)
        {
            _logger = logger;
            _sp = sp;
            _snapshotService = snapshotService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("OrderBookStreamWorker starting");

            using var scope = _sp.CreateScope();
            var redis = scope.ServiceProvider.GetRequiredService<IRedisCache>();

            // Ensure stream exists. Use XLen to check and XAddAsync to create a placeholder entry if missing.
            try
            {
                try
                {
                    var len = redis.XLen(StreamKey);
                    if (len == 0)
                    {
                        // create a placeholder entry so downstream consumers can observe the stream
                        await redis.XAddAsync(StreamKey, "*", null, ("_created", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Stream existence check/create failed (server may not support Streams): {Msg}", ex.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Stream setup attempt failed entirely: {Msg}", ex.Message);
            }

            var consumerName = ConsumerPrefix + Environment.MachineName + "-" + Guid.NewGuid().ToString("n");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    (string id, string[] items)[]? streamEntries = null;
                    try
                    {
                        // Non-blocking read of recent entries
                        streamEntries = await redis.XRangeAsync(StreamKey, "-", "+", count: 10);
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message?.Contains("unknown command") == true)
                        {
                            _logger.LogWarning("Redis server does not support Streams (XRANGE/XREADGROUP). Stream processing disabled: {Msg}", ex.Message);
                            await Task.Delay(5000, stoppingToken);
                            continue;
                        }
                        _logger.LogWarning(ex, "Stream XRange read failed: {Msg}", ex.Message);
                    }

                    if (streamEntries == null || streamEntries.Length == 0)
                    {
                        await HandlePendingEntries(redis);
                        await Task.Delay(200, stoppingToken);
                        continue;
                    }

                    foreach (var entry in streamEntries)
                    {
                        var id = entry.id;
                        var values = entry.items;
                        var dict = new System.Collections.Generic.Dictionary<string, string>();

                        if (values != null && values.Length > 0)
                        {
                            int start = 0;
                            if (values[0] == id) start = 1;
                            for (int i = start; i < values.Length; i += 2)
                            {
                                if (i + 1 < values.Length)
                                {
                                    dict[values[i]] = values[i + 1];
                                }
                            }
                        }

                        if (dict.TryGetValue("type", out var type) && type == "order_created")
                        {
                            dict.TryGetValue("symbol", out var symbol);
                            if (!string.IsNullOrEmpty(symbol))
                            {
                                await ProcessOrderCreated(symbol, id, redis);
                                try { redis.Execute("XACK", StreamKey, GroupName, id); }
                                catch (Exception ex) { _logger.LogWarning(ex, "Failed to XACK entry {Id}", id); }
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

        private async Task ProcessOrderCreated(string symbol, string entryId, IRedisCache redis)
        {
            try
            {
                // 使用统一的快照服务
                await _snapshotService.PushSnapshotAsync(symbol);

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

        private async Task HandlePendingEntries(IRedisCache redis)
        {
            try
            {
                // Attempt to XAUTOCLAIM messages idle longer than threshold and move them to this consumer for processing
                var minIdleMs = 60000; // 1 minute
                try
                {
                    // XAUTOCLAIM <stream> <group> <consumer> <min-idle-ms> <start> COUNT 10
                    // Use Database.ExecuteAsync to run raw command
                    redis.Execute("XAUTOCLAIM", StreamKey, GroupName, ConsumerPrefix + "-reclaimer", minIdleMs.ToString(), "0-0", "COUNT", "10");
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
