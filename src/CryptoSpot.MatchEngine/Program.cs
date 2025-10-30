using CryptoSpot.Application.DependencyInjection;
using CryptoSpot.Infrastructure;
using CryptoSpot.Infrastructure.Services;
using CryptoSpot.MatchEngine;
using CryptoSpot.Redis;
using CryptoSpot.Redis.Serializer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // Core hosted workers
        services.AddHostedService<MatchEngineWorker>();
        services.AddHostedService<OrderBookStreamWorker>();

        // Register only minimal infra required for standalone MatchEngine host (avoid DB-hosted services)
        services.AddRedis(context.Configuration.GetSection("Redis"));
        services.AddCleanArchitecture();

    // Redis-first matching engine removed; match logic runs inside the standalone match-engine (InMemory or other implementations)

        // Register a fallback TradingPairService to avoid MySQL dependency during match engine standalone runs
        services.AddSingleton<CryptoSpot.Application.Abstractions.Services.Trading.ITradingPairService, FallbackTradingPairService>();

    // Register InMemoryMatchEngineService as the concrete implementation for application-level IMatchEngineService
    services.AddSingleton(typeof(CryptoSpot.Application.Abstractions.Services.Trading.IMatchEngineService), typeof(InMemoryMatchEngineService));
    })
    .Build();

await host.RunAsync();
