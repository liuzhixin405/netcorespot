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

                    await dbContext.SaveChangesAsync(ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "订单批量同步失败");
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
            return new Order
            {
                Id = int.Parse(data["Id"]),
                UserId = int.Parse(data["UserId"]),
                TradingPairId = int.Parse(data["TradingPairId"]),
                OrderId = data["OrderId"],
                ClientOrderId = data.GetValueOrDefault("ClientOrderId"),
                Side = Enum.Parse<OrderSide>(data["Side"]),
                Type = Enum.Parse<OrderType>(data["Type"]),
                Status = Enum.Parse<OrderStatus>(data["Status"]),
                Quantity = decimal.Parse(data["Quantity"]),
                Price = data.ContainsKey("Price") && !string.IsNullOrEmpty(data["Price"]) ? decimal.Parse(data["Price"]) : null,
                FilledQuantity = decimal.Parse(data.GetValueOrDefault("FilledQuantity", "0")),
                AveragePrice = data.ContainsKey("AveragePrice") && !string.IsNullOrEmpty(data["AveragePrice"]) ? decimal.Parse(data["AveragePrice"]) : 0m,
                CreatedAt = long.Parse(data["CreatedAt"]),
                UpdatedAt = long.Parse(data["UpdatedAt"])
            };
        }

        private void UpdateOrderFromRedis(Order order, Dictionary<string, string> data)
        {
            order.Status = Enum.Parse<OrderStatus>(data["Status"]);
            order.FilledQuantity = decimal.Parse(data.GetValueOrDefault("FilledQuantity", "0"));
            order.AveragePrice = data.ContainsKey("AveragePrice") && !string.IsNullOrEmpty(data["AveragePrice"]) 
                ? decimal.Parse(data["AveragePrice"]) 
                : 0m;
            order.UpdatedAt = long.Parse(data["UpdatedAt"]);
        }

        private class SyncQueueItem
        {
            public int orderId { get; set; }
            public string operation { get; set; } = string.Empty;
        }
    }
