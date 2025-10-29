using CryptoSpot.Application.DependencyInjection;
using CryptoSpot.Infrastructure;
using CryptoSpot.Infrastructure.Services;
using CryptoSpot.MatchEngine;
using CryptoSpot.Redis;
using CryptoSpot.Redis.Serializer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // Core hosted workers
        services.AddHostedService<MatchEngineWorker>();
        services.AddHostedService<OrderBookStreamWorker>();

        // Register only minimal infra required for standalone MatchEngine host (avoid DB-hosted services)
        services.AddRedis(context.Configuration.GetSection("Redis"));
        services.AddCleanArchitecture();

        // Register Redis-first matching engine and adapters used by the in-memory engine
        services.AddSingleton<CryptoSpot.Infrastructure.Services.RedisOrderMatchingEngine>();
        services.AddScoped<CryptoSpot.Infrastructure.Services.RedisOrderMatchingEngineAdapter>();

        // Register a fallback TradingPairService to avoid MySQL dependency during match engine standalone runs
        services.AddSingleton<CryptoSpot.Application.Abstractions.Services.Trading.ITradingPairService, FallbackTradingPairService>();

        // Register InMemoryMatchEngineService as the concrete implementation for both local IMatchEngineService types
        services.AddSingleton(typeof(CryptoSpot.Application.Abstractions.Services.Trading.IMatchEngineService), typeof(InMemoryMatchEngineService));
        services.AddSingleton(typeof(CryptoSpot.MatchEngine.IMatchEngineService), typeof(InMemoryMatchEngineService));
    })
    .Build();

await host.RunAsync();
