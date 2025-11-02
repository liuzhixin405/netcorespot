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
using CryptoSpot.MatchEngine.Core;

namespace CryptoSpot.MatchEngine;

public class MatchEngineWorker : BackgroundService
{
    private readonly ILogger<MatchEngineWorker> _logger;
    private readonly IServiceProvider _provider;
    private const string IncomingQueueKey = "orders:incoming"; // list key

    private readonly IOrderPayloadDecoder _decoder;

    public MatchEngineWorker(ILogger<MatchEngineWorker> logger, IServiceProvider provider, IOrderPayloadDecoder decoder)
    {
        _logger = logger;
        _provider = provider;
        _decoder = decoder;
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
                var matchEngine = scope.ServiceProvider.GetRequiredService<CryptoSpot.Application.Abstractions.Services.Trading.IMatchEngineService>();

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
                            CryptoSpot.Application.DTOs.Trading.CreateOrderRequestDto? req; int userId; string? err;
                            if (!_decoder.TryDecode(payload, out req, out userId, out err) || req == null)
                            {
                                _logger.LogWarning("Invalid order payload (decoder): {Error} Preview={Preview}", err, payload.Length > 200 ? payload[..200] + "..." : payload);
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

