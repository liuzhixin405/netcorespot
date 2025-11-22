using CryptoSpot.Application.DependencyInjection;
using CryptoSpot.Infrastructure;
using CryptoSpot.Infrastructure.Extensions;
using CryptoSpot.Infrastructure.Services;
using CryptoSpot.MatchEngine;
using CryptoSpot.MatchEngine.Core;
using CryptoSpot.MatchEngine.Events;
using CryptoSpot.MatchEngine.Extensions;
using CryptoSpot.MatchEngine.Services;
using CryptoSpot.Redis;
using CryptoSpot.Redis.Serializer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddHostedService<MatchEngineWorker>();
        services.AddHostedService<OrderBookStreamWorker>();
        services.AddHostedService<OrderBookSnapshotWorker>();
        services.AddHostedService<MatchEngineEventDispatcherWorker>();

        services.AddRedis(context.Configuration.GetSection("Redis"));
        services.AddCleanArchitecture();

        // 使用基于 Redis 的交易对服务（撮合引擎不需要数据库）
        services.AddSingleton<CryptoSpot.Application.Abstractions.Services.Trading.ITradingPairService, RedisTradingPairService>();

        services.AddSingleton<ISettlementService, LuaSettlementService>();
        services.AddSingleton<IOrderPayloadDecoder, CompositeOrderPayloadDecoder>();
        services.AddSingleton<AsyncMatchEngineEventBus>();
        services.AddSingleton<IMatchEngineEventBus>(sp => sp.GetRequiredService<AsyncMatchEngineEventBus>());
        services.AddSingleton<IMatchingAlgorithm, PriceTimePriorityMatchingAlgorithm>();
        services.AddSingleton<IMatchEngineMetrics, NoOpMatchEngineMetrics>();

        services.AddSingleton<System.Collections.Concurrent.ConcurrentDictionary<string, IOrderBook>>();

        services.AddSingleton(typeof(CryptoSpot.Application.Abstractions.Services.Trading.IMatchEngineService), typeof(InMemoryMatchEngineService));

        // 添加重构后的服务
        services.AddSingleton<IOrderBookSnapshotService, OrderBookSnapshotService>();
        services.AddSingleton<ITradingPairParser, TradingPairParserService>();

        // 添加健康检查（撮合引擎专用，不包含数据库）
        services.AddMatchEngineHealthChecks(context.Configuration);
    })
    .Build();

// 启动健康检查
await host.Services.PerformStartupHealthChecks(
    host.Services.GetRequiredService<IConfiguration>(),
    componentName: "撮合引擎");

await host.RunAsync();
