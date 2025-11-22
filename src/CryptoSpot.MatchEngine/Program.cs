using CryptoSpot.Application.DependencyInjection;
using CryptoSpot.Infrastructure;
using CryptoSpot.Infrastructure.Extensions;
using CryptoSpot.Infrastructure.Services;
using CryptoSpot.MatchEngine;
using CryptoSpot.MatchEngine.Core;
using CryptoSpot.MatchEngine.Events;
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

        services.AddSingleton<CryptoSpot.Application.Abstractions.Services.Trading.ITradingPairService, FallbackTradingPairService>();

        services.AddSingleton<ISettlementService, LuaSettlementService>();
        services.AddSingleton<IOrderPayloadDecoder, CompositeOrderPayloadDecoder>();
        services.AddSingleton<AsyncMatchEngineEventBus>();
        services.AddSingleton<IMatchEngineEventBus>(sp => sp.GetRequiredService<AsyncMatchEngineEventBus>());
        services.AddSingleton<IMatchingAlgorithm, PriceTimePriorityMatchingAlgorithm>();
        services.AddSingleton<IMatchEngineMetrics, NoOpMatchEngineMetrics>();

        services.AddSingleton<System.Collections.Concurrent.ConcurrentDictionary<string, IOrderBook>>();

        services.AddSingleton(typeof(CryptoSpot.Application.Abstractions.Services.Trading.IMatchEngineService), typeof(InMemoryMatchEngineService));

        // 添加健康检查
        services.AddCryptoSpotHealthChecks(context.Configuration);
    })
    .Build();

// 启动健康检查
var logger = host.Services.GetRequiredService<ILogger<Program>>();
var configuration = host.Services.GetRequiredService<IConfiguration>();
var healthCheckEnabled = configuration.GetValue<bool>("HealthChecks:EnableStartupCheck", true);

if (healthCheckEnabled)
{
    var failFast = configuration.GetValue<bool>("HealthChecks:FailFast", true);
    var maxRetries = configuration.GetValue<int>("HealthChecks:MaxRetries", 3);
    var retryDelaySeconds = configuration.GetValue<int>("HealthChecks:RetryDelaySeconds", 5);

    logger.LogInformation("开始撮合引擎启动健康检查...");
    logger.LogInformation("配置: FailFast={FailFast}, MaxRetries={MaxRetries}, RetryDelay={RetryDelay}s",
        failFast, maxRetries, retryDelaySeconds);

    var healthCheckService = host.Services.GetRequiredService<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService>();
    
    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            logger.LogInformation("执行健康检查 (尝试 {Attempt}/{MaxRetries})...", attempt, maxRetries);
            
            var healthReport = await healthCheckService.CheckHealthAsync();
            
            if (healthReport.Status == Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy)
            {
                logger.LogInformation("✓ 所有健康检查通过");
                foreach (var entry in healthReport.Entries)
                {
                    logger.LogInformation("  ✓ {CheckName}: {Status} ({Duration}ms)",
                        entry.Key,
                        entry.Value.Status,
                        entry.Value.Duration.TotalMilliseconds);
                }
                break;
            }

            // 记录失败的检查
            logger.LogWarning("健康检查未完全通过 (状态: {Status})", healthReport.Status);
            foreach (var entry in healthReport.Entries)
            {
                if (entry.Value.Status != Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy)
                {
                    logger.LogWarning("  ✗ {CheckName}: {Status} - {Description}",
                        entry.Key,
                        entry.Value.Status,
                        entry.Value.Description);
                    
                    if (entry.Value.Exception != null)
                    {
                        logger.LogError(entry.Value.Exception, "健康检查 {CheckName} 异常", entry.Key);
                    }
                }
            }

            // 如果是最后一次尝试且配置为失败时停止
            if (attempt == maxRetries && failFast)
            {
                var errorMessage = $"撮合引擎启动健康检查失败，已重试 {maxRetries} 次";
                logger.LogCritical(errorMessage);
                throw new InvalidOperationException(errorMessage);
            }

            // 如果还有重试机会，等待后重试
            if (attempt < maxRetries)
            {
                logger.LogInformation("等待 {Delay} 秒后重试...", retryDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds));
            }
        }
        catch (Exception ex) when (attempt < maxRetries)
        {
            logger.LogError(ex, "健康检查执行失败 (尝试 {Attempt}/{MaxRetries})", attempt, maxRetries);
            
            if (attempt < maxRetries)
            {
                logger.LogInformation("等待 {Delay} 秒后重试...", retryDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds));
            }
            else if (failFast)
            {
                throw;
            }
        }
    }
}
else
{
    logger.LogWarning("⚠ 撮合引擎启动健康检查已禁用");
}

await host.RunAsync();
