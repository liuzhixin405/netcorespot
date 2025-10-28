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
                    // 1️⃣ 原子地从主队列移动到处理队列（备份），避免在转移过程中丢失项
                    var moved = await _redis.ListRightPopLeftPushBatchAsync(command.QueueKey, processingQueueKey, batchSize);
                    if (moved != null && moved.Count > 0)
                    {
                        batch.AddRange(moved);
                    }

                    // 2️⃣ 处理数据：逐项 upsert（以 Redis 为真源），并在成功后按项从 processing 列表移除对应 JSON；若批次中部分失败，恢复未处理项到主队列
                    var successfullyProcessed = new List<string>();
                    var failedItems = new List<string>();

                    foreach (var json in batch)
                    {
                        var item = JsonSerializer.Deserialize<SyncQueueItem>(json);
                        if (item == null) continue;

                        try
                        {
                            var tradeId = item.tradeId;
                            if (tradeId == 0)
                            {
                                successfullyProcessed.Add(json);
                                continue;
                            }

                            var tradeData = await _redis.HGetAllAsync($"trade:{tradeId}");
                            if (tradeData == null || tradeData.Count == 0)
                            {
                                // no data in redis: consider it processed and remove from processing queue
                                successfullyProcessed.Add(json);
                                continue;
                            }

                            var exists = await dbContext.Trades.AnyAsync(t => t.Id == tradeId, ct);
                            if (!exists)
                            {
                                var trade = MapToTrade(tradeData);
                                dbContext.Trades.Add(trade);
                                processedCount++;
                            }

                            successfullyProcessed.Add(json);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "处理成交记录同步项失败: {Json}", json);
                            failedCount++;
                            failedItems.Add(json);
                        }
                    }

                    // 3️⃣ 保存到数据库
                    await dbContext.SaveChangesAsync(ct);

                    // 4️⃣ 从 processing 列表中逐个删除已成功的项（LREM 语义）
                    foreach (var okJson in successfullyProcessed)
                    {
                        try
                        {
                            await _redis.ListRemoveAsync(processingQueueKey, okJson, 1);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "从 processing 列表删除已处理项失败: {Json}", okJson);
                        }
                    }

                    // 5️⃣ 将失败项恢复回主队列并从 processing 中删除
                    foreach (var failJson in failedItems)
                    {
                        try
                        {
                            await _redis.ListLeftPushAsync(command.QueueKey, failJson);
                            await _redis.ListRemoveAsync(processingQueueKey, failJson, 1);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "恢复失败项到主队列失败: {Json}", failJson);
                        }
                    }

                    _logger.LogDebug("✅ 成交批量同步完成: 处理={Processed}, 失败={Failed}", processedCount, failedCount);
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
