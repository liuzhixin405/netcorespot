using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.Extensions
{
    /// <summary>
    /// 启动健康检查扩展方法
    /// </summary>
    public static class StartupHealthCheckExtensions
    {
        /// <summary>
        /// 在启动时执行健康检查（用于 Worker Service / IHost）
        /// </summary>
        /// <param name="services">服务提供者</param>
        /// <param name="configuration">配置</param>
        /// <param name="loggerFactory">日志工厂（可选）</param>
        public static async Task PerformStartupHealthChecks(
            this IServiceProvider services,
            IConfiguration configuration,
            string componentName = "应用")
        {
            var logger = services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("HealthCheck");

            // 从配置读取设置
            var healthCheckConfig = configuration.GetSection("HealthChecks");
            var enableStartupCheck = healthCheckConfig.GetValue("EnableStartupCheck", true);
            
            if (!enableStartupCheck)
            {
                logger.LogInformation("{ComponentName}启动健康检查已禁用", componentName);
                return;
            }

            var shouldFailFast = healthCheckConfig.GetValue("FailFast", true);
            var maxRetryCount = healthCheckConfig.GetValue("MaxRetries", 3);
            var retryDelay = healthCheckConfig.GetValue("RetryDelaySeconds", 5);

            logger.LogInformation("开始{ComponentName}启动健康检查...", componentName);
            logger.LogInformation("配置: FailFast={FailFast}, MaxRetries={MaxRetries}, RetryDelay={RetryDelay}s",
                shouldFailFast, maxRetryCount, retryDelay);

            var healthCheckService = services.GetRequiredService<HealthCheckService>();

            for (int attempt = 1; attempt <= maxRetryCount; attempt++)
            {
                try
                {
                    logger.LogInformation("执行健康检查 (尝试 {Attempt}/{MaxRetries})...", 
                        attempt, maxRetryCount);

                    var healthReport = await healthCheckService.CheckHealthAsync();

                    if (healthReport.Status == HealthStatus.Healthy)
                    {
                        logger.LogInformation("✓ 所有健康检查通过");
                        foreach (var entry in healthReport.Entries)
                        {
                            logger.LogInformation("  ✓ {CheckName}: {Status} ({Duration}ms)",
                                entry.Key,
                                entry.Value.Status,
                                entry.Value.Duration.TotalMilliseconds);
                        }
                        return;
                    }

                    // 记录失败的检查
                    logger.LogWarning("健康检查未完全通过 (状态: {Status})", healthReport.Status);
                    foreach (var entry in healthReport.Entries)
                    {
                        if (entry.Value.Status != HealthStatus.Healthy)
                        {
                            logger.LogWarning("  ✗ {CheckName}: {Status} - {Description}",
                                entry.Key,
                                entry.Value.Status,
                                entry.Value.Description);
                            
                            if (entry.Value.Exception != null)
                            {
                                logger.LogError(entry.Value.Exception, 
                                    "健康检查 {CheckName} 异常", entry.Key);
                            }
                        }
                    }

                    // 如果是最后一次尝试且配置为失败时停止
                    if (attempt == maxRetryCount && shouldFailFast)
                    {
                        var errorMessage = $"{componentName}启动健康检查失败，已重试 {maxRetryCount} 次";
                        logger.LogCritical(errorMessage);
                        throw new InvalidOperationException(errorMessage);
                    }

                    // 如果还有重试机会，等待后重试
                    if (attempt < maxRetryCount)
                    {
                        logger.LogInformation("等待 {Delay} 秒后重试...", retryDelay);
                        await Task.Delay(TimeSpan.FromSeconds(retryDelay));
                    }
                }
                catch (Exception ex) when (attempt < maxRetryCount)
                {
                    logger.LogError(ex, "健康检查执行失败 (尝试 {Attempt}/{MaxRetries})", 
                        attempt, maxRetryCount);
                    
                    if (attempt < maxRetryCount)
                    {
                        logger.LogInformation("等待 {Delay} 秒后重试...", retryDelay);
                        await Task.Delay(TimeSpan.FromSeconds(retryDelay));
                    }
                    else if (shouldFailFast)
                    {
                        throw;
                    }
                }
            }

            // 所有重试都失败但不终止
            if (!shouldFailFast)
            {
                logger.LogWarning("{ComponentName}健康检查失败，但配置为允许启动", componentName);
            }
        }

        /// <summary>
        /// 在启动时执行健康检查（用于 ASP.NET Core）
        /// </summary>
        /// <param name="app">Web应用程序</param>
        /// <param name="configuration">配置</param>
        /// <param name="failFast">健康检查失败时是否快速失败（终止应用）</param>
        /// <param name="maxRetries">最大重试次数</param>
        /// <param name="retryDelaySeconds">重试延迟（秒）</param>
        public static async Task<IApplicationBuilder> PerformStartupHealthChecks(
            this IApplicationBuilder app,
            IConfiguration configuration,
            bool? failFast = null,
            int? maxRetries = null,
            int? retryDelaySeconds = null)
        {
            var logger = app.ApplicationServices
                .GetRequiredService<ILogger<HealthCheckService>>();

            // 从配置读取设置
            var healthCheckConfig = configuration.GetSection("HealthChecks");
            var enableStartupCheck = healthCheckConfig.GetValue("EnableStartupCheck", true);
            
            if (!enableStartupCheck)
            {
                logger.LogInformation("启动健康检查已禁用");
                return app;
            }

            var shouldFailFast = failFast ?? healthCheckConfig.GetValue("FailFast", true);
            var maxRetryCount = maxRetries ?? healthCheckConfig.GetValue("MaxRetries", 3);
            var retryDelay = retryDelaySeconds ?? healthCheckConfig.GetValue("RetryDelaySeconds", 5);

            logger.LogInformation("开始启动健康检查...");
            logger.LogInformation("配置: FailFast={FailFast}, MaxRetries={MaxRetries}, RetryDelay={RetryDelay}s",
                shouldFailFast, maxRetryCount, retryDelay);

            var healthCheckService = app.ApplicationServices
                .GetRequiredService<HealthCheckService>();

            for (int attempt = 1; attempt <= maxRetryCount; attempt++)
            {
                try
                {
                    logger.LogInformation("执行健康检查 (尝试 {Attempt}/{MaxRetries})...", 
                        attempt, maxRetryCount);

                    var healthReport = await healthCheckService.CheckHealthAsync();

                    if (healthReport.Status == HealthStatus.Healthy)
                    {
                        logger.LogInformation("✓ 所有健康检查通过");
                        foreach (var entry in healthReport.Entries)
                        {
                            logger.LogInformation("  ✓ {CheckName}: {Status} ({Duration}ms)",
                                entry.Key,
                                entry.Value.Status,
                                entry.Value.Duration.TotalMilliseconds);
                        }
                        return app;
                    }

                    // 记录失败的检查
                    logger.LogWarning("健康检查未完全通过 (状态: {Status})", healthReport.Status);
                    foreach (var entry in healthReport.Entries)
                    {
                        if (entry.Value.Status != HealthStatus.Healthy)
                        {
                            logger.LogWarning("  ✗ {CheckName}: {Status} - {Description}",
                                entry.Key,
                                entry.Value.Status,
                                entry.Value.Description);
                            
                            if (entry.Value.Exception != null)
                            {
                                logger.LogError(entry.Value.Exception, 
                                    "健康检查 {CheckName} 异常", entry.Key);
                            }
                        }
                    }

                    // 如果不是最后一次尝试，等待后重试
                    if (attempt < maxRetryCount)
                    {
                        logger.LogInformation("等待 {Delay} 秒后重试...", retryDelay);
                        await Task.Delay(TimeSpan.FromSeconds(retryDelay));
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "健康检查执行失败 (尝试 {Attempt}/{MaxRetries})", 
                        attempt, maxRetryCount);
                    
                    if (attempt < maxRetryCount)
                    {
                        logger.LogInformation("等待 {Delay} 秒后重试...", retryDelay);
                        await Task.Delay(TimeSpan.FromSeconds(retryDelay));
                    }
                }
            }

            // 所有重试都失败
            var errorMessage = $"启动健康检查失败，已重试 {maxRetryCount} 次";
            logger.LogError(errorMessage);

            if (shouldFailFast)
            {
                logger.LogCritical("应用程序将终止（FailFast=true）");
                throw new InvalidOperationException(errorMessage);
            }
            else
            {
                logger.LogWarning("应用程序将继续运行（FailFast=false）");
            }

            return app;
        }
    }
}
