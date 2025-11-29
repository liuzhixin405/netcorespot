using CryptoSpot.Application.DependencyInjection;
using CryptoSpot.MatchEngine;
using CryptoSpot.MatchEngine.Core;
using CryptoSpot.MatchEngine.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // 清理架构基础设施
        services.AddCleanArchitecture();

        // 核心撮合引擎服务
        services.AddSingleton<CryptoSpot.Application.Abstractions.Services.Trading.IMatchEngineService, ChannelMatchEngineService>();
        
        // 撮合引擎组件
        services.AddSingleton<IMatchingAlgorithm, PriceTimePriorityMatchingAlgorithm>();
        services.AddSingleton<ITradingPairParser, TradingPairParserService>();
        services.AddSingleton<IMatchEngineMetrics, NoOpMatchEngineMetrics>();
        
        // 内存资产存储
        services.AddSingleton<InMemoryAssetStore>();
        
        // 订单簿存储
        services.AddSingleton<System.Collections.Concurrent.ConcurrentDictionary<string, IOrderBook>>();
    })
    .Build();

await host.RunAsync();
