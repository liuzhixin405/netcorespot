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

                try
                {
                    for (int i = 0; i < batchSize; i++)
                    {
                        var json = await _redis.ListRightPopAsync(command.QueueKey);
                        if (string.IsNullOrEmpty(json)) break;

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

                    await dbContext.SaveChangesAsync(ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "成交记录批量同步失败");
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
            return new Trade
            {
                Id = int.Parse(data["Id"]),
                TradingPairId = int.Parse(data["TradingPairId"]),
                TradeId = data["TradeId"],
                BuyOrderId = int.Parse(data["BuyOrderId"]),
                SellOrderId = int.Parse(data["SellOrderId"]),
                BuyerId = int.Parse(data["BuyerId"]),
                SellerId = int.Parse(data["SellerId"]),
                Price = decimal.Parse(data["Price"]),
                Quantity = decimal.Parse(data["Quantity"]),
                Fee = data.ContainsKey("Fee") ? decimal.Parse(data["Fee"]) : 0m,
                FeeAsset = data.GetValueOrDefault("FeeAsset", ""),
                ExecutedAt = long.Parse(data["ExecutedAt"])
            };
        }

        private class SyncQueueItem
        {
            public int tradeId { get; set; }
        }
    }
