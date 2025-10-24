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
/// 资产同步命令处理器（高频批量处理）
/// </summary>
    public class SyncAssetsCommandHandler : ICommandHandler<SyncAssetsCommand, SyncAssetsResult>
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly IRedisCache _redis;
        private readonly ILogger<SyncAssetsCommandHandler> _logger;

        public SyncAssetsCommandHandler(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            IRedisCache redis,
            ILogger<SyncAssetsCommandHandler> logger)
        {
            _dbContextFactory = dbContextFactory;
            _redis = redis;
            _logger = logger;
        }

        public async Task<SyncAssetsResult> HandleAsync(SyncAssetsCommand command, CancellationToken ct = default)
        {
            var stopwatch = Stopwatch.StartNew();
            var processedCount = 0;
            var failedCount = 0;

            try
            {
                var queueLength = await _redis.ListLengthAsync(command.QueueKey);
                if (queueLength == 0)
                {
                    return new SyncAssetsResult
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

                // 用于批量更新资产（去重）
                var assetsToUpdate = new Dictionary<string, (int userId, string currency)>();

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

                    // 2️⃣ 收集需要更新的资产（去重）
                    foreach (var json in batch)
                    {
                        var item = JsonSerializer.Deserialize<SyncQueueItem>(json);
                        if (item == null) continue;

                        var key = $"{item.userId}:{item.symbol}";
                        assetsToUpdate[key] = (item.userId, item.symbol);
                    }

                    // 3️⃣ 批量从 Redis 读取最新资产数据并更新到 MySQL
                    foreach (var (key, (userId, symbol)) in assetsToUpdate)
                    {
                        try
                        {
                            var redisKey = $"asset:{userId}:{symbol}";
                            var assetData = await _redis.HGetAllAsync(redisKey);
                            if (assetData == null || assetData.Count == 0) continue;

                            var asset = await dbContext.Assets.FirstOrDefaultAsync(
                                a => a.UserId == userId && a.Symbol == symbol, ct);

                            if (asset == null)
                            {
                                // 创建新资产
                                asset = MapToAsset(assetData);
                                dbContext.Assets.Add(asset);
                            }
                            else
                            {
                                // 更新现有资产
                                UpdateAssetFromRedis(asset, assetData);
                                dbContext.Assets.Update(asset);
                            }

                            processedCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "处理资产同步失败: UserId={UserId}, Symbol={Symbol}", userId, symbol);
                            failedCount++;
                        }
                    }

                    // 4️⃣ 保存到数据库
                    await dbContext.SaveChangesAsync(ct);

                    // 5️⃣ 成功后清理备份队列
                    await _redis.RemoveAsync(processingQueueKey);

                    _logger.LogDebug("✅ 资产批量同步成功: 处理={Processed}, 失败={Failed}", processedCount, failedCount);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ 资产批量同步失败，正在恢复数据...");

                    // 6️⃣ 失败后从备份队列恢复到主队列
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

                    return new SyncAssetsResult
                    {
                        Success = false,
                        ProcessedCount = processedCount,
                        FailedCount = failedCount,
                        ErrorMessage = ex.Message,
                        ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                    };
                }

                stopwatch.Stop();
                return new SyncAssetsResult
                {
                    Success = true,
                    ProcessedCount = processedCount,
                    FailedCount = failedCount,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "资产同步命令处理失败");
                return new SyncAssetsResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                };
            }
        }

        private Asset MapToAsset(Dictionary<string, string> data)
        {
            // ✅ 修复：Redis 中的字段名是小写的 availableBalance/frozenBalance（精度为 long）
            var availableBalance = long.Parse(data["availableBalance"]);
            var frozenBalance = long.Parse(data["frozenBalance"]);
            const long PRECISION = 100_000_000; // 8 位小数精度
            
            return new Asset
            {
                Id = int.Parse(data.GetValueOrDefault("id", "0")),
                UserId = int.Parse(data["userId"]),
                Symbol = data["symbol"],
                Available = (decimal)availableBalance / PRECISION,
                Frozen = (decimal)frozenBalance / PRECISION,
                // Total 是计算属性，不需要赋值
                UpdatedAt = long.Parse(data["updatedAt"])
            };
        }

        private void UpdateAssetFromRedis(Asset asset, Dictionary<string, string> data)
        {
            // ✅ 修复：Redis 中的字段名是小写的 availableBalance/frozenBalance（精度为 long）
            var availableBalance = long.Parse(data["availableBalance"]);
            var frozenBalance = long.Parse(data["frozenBalance"]);
            const long PRECISION = 100_000_000; // 8 位小数精度
            
            asset.Available = (decimal)availableBalance / PRECISION;
            asset.Frozen = (decimal)frozenBalance / PRECISION;
            // Total 是计算属性（Available + Frozen），不需要赋值
            asset.UpdatedAt = long.Parse(data["updatedAt"]);
        }

        private class SyncQueueItem
        {
            public int userId { get; set; }
            public string symbol { get; set; } = string.Empty;
        }
    }
