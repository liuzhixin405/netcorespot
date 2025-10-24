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
/// 订单同步命令处理器（高频批量处理）
/// </summary>
    public class SyncOrdersCommandHandler : ICommandHandler<SyncOrdersCommand, SyncOrdersResult>
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly IRedisCache _redis;
        private readonly ILogger<SyncOrdersCommandHandler> _logger;
        private const long PRECISION = 100000000;

        public SyncOrdersCommandHandler(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            IRedisCache redis,
            ILogger<SyncOrdersCommandHandler> logger)
        {
            _dbContextFactory = dbContextFactory;
            _redis = redis;
            _logger = logger;
        }

        public async Task<SyncOrdersResult> HandleAsync(SyncOrdersCommand command, CancellationToken ct = default)
        {
            var stopwatch = Stopwatch.StartNew();
            var processedCount = 0;
            var failedCount = 0;

            try
            {
                var queueLength = await _redis.ListLengthAsync(command.QueueKey);
                if (queueLength == 0)
                {
                    return new SyncOrdersResult
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
                            var orderId = item.orderId;
                            var operation = item.operation;

                            // 从 Redis 读取最新订单数据
                            var orderData = await _redis.HGetAllAsync($"order:{orderId}");
                            if (orderData == null || orderData.Count == 0) continue;

                            if (operation == "CREATE")
                            {
                                var exists = await dbContext.Orders.AnyAsync(o => o.Id == orderId, ct);
                                if (!exists)
                                {
                                    var order = MapToOrder(orderData);
                                    dbContext.Orders.Add(order);
                                }
                            }
                            else if (operation == "UPDATE")
                            {
                                var order = await dbContext.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
                                if (order != null)
                                {
                                    UpdateOrderFromRedis(order, orderData);
                                    dbContext.Orders.Update(order);
                                }
                            }
                            else if (operation == "DELETE")
                            {
                                var order = await dbContext.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
                                if (order != null)
                                {
                                    dbContext.Orders.Remove(order);
                                }
                            }

                            processedCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "处理订单同步项失败: {Json}", json);
                            failedCount++;
                        }
                    }

                    // 3️⃣ 保存到数据库
                    await dbContext.SaveChangesAsync(ct);

                    // 4️⃣ 成功后清理备份队列
                    await _redis.RemoveAsync(processingQueueKey);

                    _logger.LogDebug("✅ 订单批量同步成功: 处理={Processed}, 失败={Failed}", processedCount, failedCount);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ 订单批量同步失败，正在恢复数据...");

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

                    return new SyncOrdersResult
                    {
                        Success = false,
                        ProcessedCount = processedCount,
                        FailedCount = failedCount,
                        ErrorMessage = ex.Message,
                        ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                    };
                }

                stopwatch.Stop();
                return new SyncOrdersResult
                {
                    Success = true,
                    ProcessedCount = processedCount,
                    FailedCount = failedCount,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "订单同步命令处理失败");
                return new SyncOrdersResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds
                };
            }
        }

        private Order MapToOrder(Dictionary<string, string> data)
        {
            // ✅ 修复：Redis 中的字段名都是小写的
            return new Order
            {
                Id = int.Parse(data["id"]),
                UserId = int.Parse(data["userId"]),
                TradingPairId = int.Parse(data["tradingPairId"]),
                OrderId = data.GetValueOrDefault("orderId", ""),
                ClientOrderId = data.GetValueOrDefault("clientOrderId"),
                Side = (OrderSide)int.Parse(data["side"]),
                Type = (OrderType)int.Parse(data["type"]),
                Status = (OrderStatus)int.Parse(data["status"]),
                Quantity = decimal.Parse(data["quantity"]),
                Price = data.ContainsKey("price") && !string.IsNullOrEmpty(data["price"]) && data["price"] != "0" 
                    ? decimal.Parse(data["price"]) 
                    : null,
                FilledQuantity = decimal.Parse(data.GetValueOrDefault("filledQuantity", "0")),
                AveragePrice = data.ContainsKey("averagePrice") && !string.IsNullOrEmpty(data["averagePrice"]) 
                    ? decimal.Parse(data["averagePrice"]) 
                    : 0m,
                CreatedAt = long.Parse(data["createdAt"]),
                UpdatedAt = long.Parse(data["updatedAt"])
            };
        }

        private void UpdateOrderFromRedis(Order order, Dictionary<string, string> data)
        {
            // ✅ 修复：Redis 中的字段名都是小写的
            order.Status = (OrderStatus)int.Parse(data["status"]);
            order.FilledQuantity = decimal.Parse(data.GetValueOrDefault("filledQuantity", "0"));
            order.AveragePrice = data.ContainsKey("averagePrice") && !string.IsNullOrEmpty(data["averagePrice"]) 
                ? decimal.Parse(data["averagePrice"]) 
                : 0m;
            order.UpdatedAt = long.Parse(data["updatedAt"]);
        }

        private class SyncQueueItem
        {
            public int orderId { get; set; }
            public string operation { get; set; } = string.Empty;
        }
    }
