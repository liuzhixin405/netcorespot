using CryptoSpot.Application.DomainCommands.DataSync;
using CryptoSpot.Bus.Core;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Domain.Extensions;
using CryptoSpot.Persistence.Data;
using CryptoSpot.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace CryptoSpot.Infrastructure.CommandHandlers.DataSync;

/// <summary>
/// 成交记录同步命令处理器（高频批量处理）
/// </summary>
    public class SyncTradesCommandHandler : ICommandHandler<SyncTradesCommand, SyncTradesResult>
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly IRedisCache _redis;
        private readonly ILogger<SyncTradesCommandHandler> _logger;

        public SyncTradesCommandHandler(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            IRedisCache redis,
            ILogger<SyncTradesCommandHandler> logger)
        {
            _dbContextFactory = dbContextFactory;
            _redis = redis;
            _logger = logger;
        }

        public async Task<SyncTradesResult> HandleAsync(SyncTradesCommand command, CancellationToken ct = default)
        {
            var stopwatch = Stopwatch.StartNew();
            var processedCount = 0;
            var failedCount = 0;

            try
            {
                var queueLength = await _redis.ListLengthAsync(command.QueueKey);
                if (queueLength == 0)
                {
                    return new SyncTradesResult
                    {
                        Success = true,
                        ProcessedCount = 0,
                        ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                    };
                }

                await using var dbContext = await _dbContextFactory.CreateDbContextAsync(ct);
                var batchSize = Math.Min((int)queueLength, command.BatchSize);

                // 🔒 使用备份队列机制，防止 SaveChanges 失败导致数据丢失
                var processingQueueKey = $"{command.QueueKey}:processing";
                var batch = new List<string>();

                try
                {
                    // 1️⃣ 从主队列转移到处理队列（备份）
                    for (int i = 0; i < batchSize; i++)
                    {
                        var json = await _redis.ListRightPopAsync(command.QueueKey);
                        if (string.IsNullOrEmpty(json)) break;

                        batch.Add(json);
                        await _redis.ListLeftPushAsync(processingQueueKey, json);
                    }

                    // 2️⃣ 处理数据
                    foreach (var json in batch)
                    {
                        var item = JsonSerializer.Deserialize<SyncQueueItem>(json);
                        if (item == null) continue;

                        try
                        {
                            var tradeId = item.tradeId;
                            if (tradeId == 0) continue;

                            // 从 Redis 读取最新成交数据
                            var tradeData = await _redis.HGetAllAsync($"trade:{tradeId}");
                            if (tradeData == null || tradeData.Count == 0) continue;

                            // 检查是否已存在
                            var exists = await dbContext.Trades.AnyAsync(t => t.Id == tradeId, ct);
                            if (!exists)
                            {
                                var trade = MapToTrade(tradeData);
                                dbContext.Trades.Add(trade);
                                processedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "处理成交记录同步项失败: {Json}", json);
                            failedCount++;
                        }
                    }

                    // 3️⃣ 保存到数据库
                    await dbContext.SaveChangesAsync(ct);

                    // 4️⃣ 成功后清理备份队列
                    await _redis.RemoveAsync(processingQueueKey);

                    _logger.LogDebug("✅ 成交批量同步成功: 处理={Processed}, 失败={Failed}", processedCount, failedCount);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ 成交批量同步失败，正在恢复数据...");

                    // 5️⃣ 失败后从备份队列恢复到主队列
                    var recoveredCount = 0;
                    while (await _redis.ListLengthAsync(processingQueueKey) > 0)
                    {
                        var json = await _redis.ListRightPopAsync(processingQueueKey);
                        if (!string.IsNullOrEmpty(json))
                        {
                            await _redis.ListLeftPushAsync(command.QueueKey, json);
                            recoveredCount++;
                        }
                    }

                    _logger.LogWarning("⚠️ 已恢复 {Count} 条数据到主队列", recoveredCount);

                    return new SyncTradesResult
                    {
                        Success = false,
                        ProcessedCount = processedCount,
                        FailedCount = failedCount,
                        ErrorMessage = ex.Message,
                        ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                    };
                }

                stopwatch.Stop();
                return new SyncTradesResult
                {
                    Success = true,
                    ProcessedCount = processedCount,
                    FailedCount = failedCount,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "成交记录同步命令处理失败");
                return new SyncTradesResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                };
            }
        }

        private Trade MapToTrade(Dictionary<string, string> data)
        {
            // ✅ 修复：Redis 中的字段名都是小写的
            return new Trade
            {
                Id = int.Parse(data["id"]),
                TradingPairId = int.Parse(data["tradingPairId"]),
                TradeId = data.GetValueOrDefault("tradeId", ""),
                BuyOrderId = int.Parse(data["buyOrderId"]),
                SellOrderId = int.Parse(data["sellOrderId"]),
                BuyerId = int.Parse(data["buyerId"]),
                SellerId = int.Parse(data["sellerId"]),
                Price = decimal.Parse(data["price"]),
                Quantity = decimal.Parse(data["quantity"]),
                Fee = data.ContainsKey("fee") ? decimal.Parse(data["fee"]) : 0m,
                FeeAsset = data.GetValueOrDefault("feeAsset", ""),
                ExecutedAt = long.Parse(data["executedAt"])
            };
        }

        private class SyncQueueItem
        {
            public int tradeId { get; set; }
        }
    }
