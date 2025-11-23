using CryptoSpot.Application.DependencyInjection;
using CryptoSpot.Infrastructure.Extensions; // 仅用于健康检查扩展方法
using CryptoSpot.MatchEngine;
using CryptoSpot.MatchEngine.Core;
using CryptoSpot.MatchEngine.Extensions;
using CryptoSpot.MatchEngine.Services;
using CryptoSpot.MatchEngine.CommandHandlers;
using CryptoSpot.MatchEngine.Commands;
using CryptoSpot.Bus.Core;
using CryptoSpot.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // 后台服务
        services.AddHostedService<MatchEngineWorker>();
        services.AddHostedService<OrderBookStreamWorker>();

        // 基础设施
        services.AddRedis(context.Configuration.GetSection("Redis"));
        services.AddCleanArchitecture();  // 包含 CommandBus 注册

        // 核心服务
        services.AddSingleton<CryptoSpot.Application.Abstractions.Services.Trading.ITradingPairService, RedisTradingPairService>();
        services.AddSingleton<CryptoSpot.Application.Abstractions.Services.Trading.IMatchEngineService, InMemoryMatchEngineService>();
        
        // 撮合引擎组件
        services.AddSingleton<ISettlementService, LuaSettlementService>();
        services.AddSingleton<IMatchingAlgorithm, PriceTimePriorityMatchingAlgorithm>();
        services.AddSingleton<IOrderPayloadDecoder, CompositeOrderPayloadDecoder>();
        
        // 撮合引擎 Command Handlers（使用统一的 CommandBus）
        services.AddScoped<ICommandHandler<OrderPlacedCommand, bool>, OrderPlacedCommandHandler>();
        services.AddScoped<ICommandHandler<TradeExecutedCommand, bool>, TradeExecutedCommandHandler>();
        services.AddScoped<ICommandHandler<OrderBookChangedCommand, bool>, OrderBookChangedCommandHandler>();
        services.AddScoped<ICommandHandler<OrderCancelledCommand, bool>, OrderCancelledCommandHandler>();
        
        // 辅助服务
        services.AddSingleton<IOrderBookSnapshotService, OrderBookSnapshotService>();
        services.AddSingleton<ITradingPairParser, TradingPairParserService>();
        services.AddSingleton<IMatchEngineMetrics, NoOpMatchEngineMetrics>();
        
        // 订单簿存储
        services.AddSingleton<System.Collections.Concurrent.ConcurrentDictionary<string, IOrderBook>>();

        // 健康检查（撮合引擎专用，不包含数据库）
        services.AddMatchEngineHealthChecks(context.Configuration);
    })
    .Build();

// 启动健康检查
await host.Services.PerformStartupHealthChecks(
    host.Services.GetRequiredService<IConfiguration>(),
    componentName: "撮合引擎");

await host.RunAsync();
