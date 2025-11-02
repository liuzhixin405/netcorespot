using CryptoSpot.Application.DependencyInjection;
using CryptoSpot.Infrastructure;
using CryptoSpot.Infrastructure.Services;
using CryptoSpot.MatchEngine;
using CryptoSpot.Redis;
using CryptoSpot.Redis.Serializer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CryptoSpot.MatchEngine.Core;
using CryptoSpot.MatchEngine.Events;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
    // Core hosted workers
    services.AddHostedService<MatchEngineWorker>();
    services.AddHostedService<OrderBookStreamWorker>();
    services.AddHostedService<OrderBookSnapshotWorker>();
    services.AddHostedService<MatchEngineEventDispatcherWorker>();
    // 旧版订单簿推送 Worker 已弃用（OrderBookPushWorker），如果仍需对比，可临时恢复注册。
    // services.AddHostedService<OrderBookPushWorker>(); // [Obsolete]

        // Register only minimal infra required for standalone MatchEngine host (avoid DB-hosted services)
        services.AddRedis(context.Configuration.GetSection("Redis"));
        services.AddCleanArchitecture();

    // Redis-first matching engine removed; match logic runs inside the standalone match-engine (InMemory or other implementations)

        // Register a fallback TradingPairService to avoid MySQL dependency during match engine standalone runs
        services.AddSingleton<CryptoSpot.Application.Abstractions.Services.Trading.ITradingPairService, FallbackTradingPairService>();

        // Register abstractions introduced in phase-1 refactor
    services.AddSingleton<ISettlementService, LuaSettlementService>();
    services.AddSingleton<IOrderPayloadDecoder, CompositeOrderPayloadDecoder>();
    services.AddSingleton<AsyncMatchEngineEventBus>();
    services.AddSingleton<IMatchEngineEventBus>(sp => sp.GetRequiredService<AsyncMatchEngineEventBus>());
    services.AddSingleton<IMatchingAlgorithm, PriceTimePriorityMatchingAlgorithm>();
    services.AddSingleton<IMatchEngineMetrics, NoOpMatchEngineMetrics>();

        // In-memory orderbook factory (symbol -> orderbook)
    services.AddSingleton<System.Collections.Concurrent.ConcurrentDictionary<string, IOrderBook>>();

    // 注册当前内存撮合实现 (IMatchEngineService)
    services.AddSingleton(typeof(CryptoSpot.Application.Abstractions.Services.Trading.IMatchEngineService), typeof(InMemoryMatchEngineService));
    })
    .Build();

await host.RunAsync();
