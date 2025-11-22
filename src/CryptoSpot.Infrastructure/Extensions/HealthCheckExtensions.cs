using CryptoSpot.Infrastructure.HealthChecks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CryptoSpot.Infrastructure.Extensions
{
    /// <summary>
    /// 健康检查扩展方法
    /// </summary>
    public static class HealthCheckExtensions
    {
        /// <summary>
        /// 添加 CryptoSpot 健康检查
        /// </summary>
        public static IServiceCollection AddCryptoSpotHealthChecks(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var healthChecksBuilder = services.AddHealthChecks();

            // 添加数据库健康检查
            healthChecksBuilder
                .AddCheck<DatabaseHealthCheck>(
                    "database",
                    tags: new[] { "db", "sql", "ready" });

            // 添加 Redis 健康检查
            healthChecksBuilder
                .AddCheck<RedisHealthCheck>(
                    "redis",
                    tags: new[] { "cache", "redis", "ready" });

            // 添加撮合引擎健康检查
            healthChecksBuilder
                .AddCheck<MatchEngineHealthCheck>(
                    "match_engine",
                    tags: new[] { "match_engine", "ready" });

            return services;
        }

        /// <summary>
        /// 映射健康检查端点
        /// </summary>
        public static IEndpointRouteBuilder MapCryptoSpotHealthChecks(
            this IEndpointRouteBuilder endpoints)
        {
            // 完整健康检查
            endpoints.MapHealthChecks("/health", new HealthCheckOptions
            {
                ResponseWriter = HealthCheckResponseWriter.WriteResponse,
                ResultStatusCodes =
                {
                    [HealthStatus.Healthy] = StatusCodes.Status200OK,
                    [HealthStatus.Degraded] = StatusCodes.Status200OK,
                    [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
                }
            });

            // 就绪检查（用于 Kubernetes readiness probe）
            endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("ready"),
                ResponseWriter = HealthCheckResponseWriter.WriteResponse,
                ResultStatusCodes =
                {
                    [HealthStatus.Healthy] = StatusCodes.Status200OK,
                    [HealthStatus.Degraded] = StatusCodes.Status200OK,
                    [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
                }
            });

            // 存活检查（用于 Kubernetes liveness probe）
            endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
            {
                Predicate = _ => false, // 只检查服务是否响应
                ResponseWriter = HealthCheckResponseWriter.WriteResponse
            });

            return endpoints;
        }
    }
}
