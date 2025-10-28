using CryptoSpot.Application.DomainCommands.DataSync;
using CryptoSpot.Bus.Core;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Domain.Extensions;
using CryptoSpot.Persistence.Data;
using CryptoSpot.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
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
        private readonly IConfiguration _config;
        private const long PRECISION = 100000000;

        public SyncOrdersCommandHandler(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            IRedisCache redis,
            ILogger<SyncOrdersCommandHandler> logger,
            IConfiguration config)
        {
            _dbContextFactory = dbContextFactory;
            _redis = redis;
            _logger = logger;
            _config = config;
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
                // 从配置读取参数（优先级：command 指定 -> appsettings -> 默认）
                var configuredBatch = command.BatchSize > 0 ? command.BatchSize : _config.GetValue<int?>("SyncOrders:BatchSize") ?? 100;
                var batchSize = Math.Min((int)queueLength, configuredBatch);
                var rateLimitPerSecond = _config.GetValue<int?>("SyncOrders:RateLimitPerSecond") ?? 0; // 0 表示不限速
                var perItemDelayMs = rateLimitPerSecond > 0 ? (int)Math.Ceiling(1000.0 / rateLimitPerSecond) : 0;

                // 🔒 使用备份队列机制，防止 SaveChanges 失败导致数据丢失
                var processingQueueKey = $"{command.QueueKey}:processing";
                var batch = new List<string>();

                try
                {
                    // 1️⃣ 原子地从主队列移动到处理队列（备份），避免在转移过程中丢失项
                    var moved = await _redis.ListRightPopLeftPushBatchAsync(command.QueueKey, processingQueueKey, batchSize);
                    if (moved != null && moved.Count > 0)
                    {
                        // moved 按 RPOP 的顺序返回（先弹出的在前），为了按处理顺序处理我们直接使用 moved 列表
                        batch.AddRange(moved);
                    }

                    // 2️⃣ 处理数据：逐项 upsert（以 Redis 为真源），并在成功后按项从 processing 列表移除对应 JSON；若部分失败，恢复未处理项到主队列
                    var successfullyProcessed = new List<string>();
                    var failedItems = new List<string>();

                    foreach (var json in batch)
                    {
                        var item = JsonSerializer.Deserialize<SyncQueueItem>(json);
                        if (item == null) continue;

                        try
                        {
                            var orderId = item.orderId;
                            var operation = item.operation;
                            if (orderId == 0)
                            {
                                // 无效 id，视为已处理
                                successfullyProcessed.Add(json);
                                continue;
                            }

                            // 从 Redis 读取最新订单数据
                            var orderData = await _redis.HGetAllAsync($"order:{orderId}");
                            if (orderData == null || orderData.Count == 0)
                            {
                                // Redis 无数据，视为已处理（可能已被其它流程清理）
                                successfullyProcessed.Add(json);
                                continue;
                            }

                            if (operation == "CREATE")
                            {
                                var exists = await dbContext.Orders.AnyAsync(o => o.Id == orderId, ct);
                                if (!exists)
                                {
                                    var order = MapToOrder(orderData);
                                    dbContext.Orders.Add(order);
                                    processedCount++;
                                }
                            }
                            else if (operation == "UPDATE")
                            {
                                var order = await dbContext.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
                                if (order != null)
                                {
                                    UpdateOrderFromRedis(order, orderData);
                                    dbContext.Orders.Update(order);
                                    processedCount++;
                                }
                            }
                            else if (operation == "DELETE")
                            {
                                var order = await dbContext.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
                                if (order != null)
                                {
                                    dbContext.Orders.Remove(order);
                                    processedCount++;
                                }
                            }

                            successfullyProcessed.Add(json);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "处理订单同步项失败: {Json}", json);
                            failedCount++;
                            failedItems.Add(json);
                        }
                    }

                    // 3️⃣ 保存到数据库
                    await dbContext.SaveChangesAsync(ct);

                    // 记录批次指标
                    _logger.LogInformation("SyncOrders: Batch processed. BatchSize={BatchSize} Processed={Processed} Failed={Failed}", batchSize, processedCount, failedCount);

                    // 4️⃣ 从 processing 列表中逐个删除已成功的项（LREM 语义）
                    foreach (var okJson in successfullyProcessed)
                    {
                        try
                        {
                            await _redis.ListRemoveAsync(processingQueueKey, okJson, 1);
                            // 如果配置了速率限制，在线程间加入微小延迟以限制落库速度
                            if (perItemDelayMs > 0)
                            {
                                await Task.Delay(perItemDelayMs, ct);
                            }
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

                    _logger.LogDebug("✅ 订单批量同步完成: 处理={Processed}, 失败={Failed}", processedCount, failedCount);
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
                OrderId = string.IsNullOrEmpty(data.GetValueOrDefault("orderId", "")) ? $"ORD_{data.GetValueOrDefault("id", "0")}" : data.GetValueOrDefault("orderId", ""),
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
