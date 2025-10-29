using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CryptoSpot.Redis;
using System.Text.Json;
using CryptoSpot.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using MessagePack;
using MessagePack.Resolvers;

namespace CryptoSpot.MatchEngine;

public class MatchEngineWorker : BackgroundService
{
    private readonly ILogger<MatchEngineWorker> _logger;
    private readonly IServiceProvider _provider;
    private const string IncomingQueueKey = "orders:incoming"; // list key

    public MatchEngineWorker(ILogger<MatchEngineWorker> logger, IServiceProvider provider)
    {
        _logger = logger;
        _provider = provider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MatchEngine worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _provider.CreateScope();
                var redis = scope.ServiceProvider.GetRequiredService<CryptoSpot.Redis.IRedisService>();
                var matchEngine = scope.ServiceProvider.GetRequiredService<IMatchEngineService>();

                try
                {
                    // batch pop up to 50 items
                    var items = await redis.BatchRPopAsync<string>(IncomingQueueKey, 50);
                    if (items == null || items.Count == 0)
                    {
                        await Task.Delay(200, stoppingToken);
                        continue;
                    }

                    var dlqList = new List<string>();

                    foreach (var payload in items)
                    {
                        try
                        {
                            // Try JSON DTO
                            CryptoSpot.Application.DTOs.Trading.CreateOrderRequestDto req = null;
                            int userId = 0;

                            try
                            {
                                req = JsonSerializer.Deserialize<CryptoSpot.Application.DTOs.Trading.CreateOrderRequestDto>(payload);
                            }
                            catch { req = null; }

                            // If JSON failed, try envelope JSON with userId and payload/order/data
                            if (req == null)
                            {
                                try
                                {
                                    using var doc = JsonDocument.Parse(payload);
                                    var root = doc.RootElement;
                                    if (root.TryGetProperty("userId", out var u) && u.ValueKind == JsonValueKind.Number)
                                    {
                                        userId = u.GetInt32();
                                    }

                                    JsonElement? orderEl = null;
                                    if (root.TryGetProperty("order", out var o)) orderEl = o;
                                    else if (root.TryGetProperty("payload", out var p)) orderEl = p;
                                    else if (root.TryGetProperty("data", out var d)) orderEl = d;

                                    if (orderEl.HasValue)
                                    {
                                        req = JsonSerializer.Deserialize<CryptoSpot.Application.DTOs.Trading.CreateOrderRequestDto>(orderEl.Value.GetRawText());
                                    }
                                }
                                catch { /* ignore */ }
                            }

                            // If still null, try base64 MessagePack
                            if (req == null)
                            {
                                try
                                {
                                    var bytes = Convert.FromBase64String(payload);
                                    var options = MessagePackSerializerOptions.Standard
                                        .WithResolver(ContractlessStandardResolver.Instance)
                                        .WithCompression(MessagePackCompression.Lz4BlockArray);
                                    req = MessagePackSerializer.Deserialize<CryptoSpot.Application.DTOs.Trading.CreateOrderRequestDto>(bytes, options);
                                }
                                catch { req = null; }
                            }

                            if (req == null)
                            {
                                _logger.LogWarning("Invalid order payload (skipping): {Preview}", payload.Length > 200 ? payload[..200] + "..." : payload);
                                dlqList.Add(payload);
                                continue;
                            }

                            var tradingPairSvc = scope.ServiceProvider.GetRequiredService<CryptoSpot.Application.Abstractions.Services.Trading.ITradingPairService>();
                            var mapSvc = scope.ServiceProvider.GetRequiredService<CryptoSpot.Application.Mapping.IDtoMappingService>();

                            var tpResp = await tradingPairSvc.GetTradingPairIdAsync(req.Symbol);
                            var tradingPairId = tpResp.Data;
                            if (tradingPairId == 0)
                            {
                                _logger.LogWarning("TradingPair not found for symbol '{Symbol}' - DLQ", req.Symbol);
                                dlqList.Add(payload);
                                continue;
                            }

                            var orderDomain = mapSvc.MapToDomain(req, userId, tradingPairId);
                            _logger.LogInformation("Placing order via match engine: UserId={UserId} Symbol={Symbol} Side={Side} Qty={Qty} Price={Price}",
                                orderDomain.UserId, req.Symbol, req.Side, req.Quantity, req.Price);

                            try
                            {
                                var placed = await matchEngine.PlaceOrderAsync(orderDomain, req.Symbol);
                                _logger.LogInformation("Order processed by match engine: OrderId={OrderId} UserId={UserId}", placed.Id, placed.UserId);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Match engine failed for payload preview: {Preview}", payload.Length > 200 ? payload[..200] + "..." : payload);
                                dlqList.Add(payload);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to process order payload: {PayloadPreview}", payload.Length > 200 ? payload[..200] + "..." : payload);
                            dlqList.Add(payload);
                        }
                    }

                    if (dlqList.Count > 0)
                    {
                        await redis.BatchLPushAsync(IncomingQueueKey + ":dlq", dlqList);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Batch pop error");
                    await Task.Delay(1000, stoppingToken);
                }
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "MatchEngine encountered error");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}

