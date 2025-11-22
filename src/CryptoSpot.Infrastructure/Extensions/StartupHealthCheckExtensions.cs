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
        /// 在启动时执行健康检查
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
