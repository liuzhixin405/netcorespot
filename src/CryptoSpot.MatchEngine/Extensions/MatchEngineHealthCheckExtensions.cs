using CryptoSpot.Infrastructure.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoSpot.MatchEngine.Extensions
{
    /// <summary>
    /// 撮合引擎专用健康检查扩展
    /// 只检查 Redis 和撮合引擎队列，不检查数据库
    /// </summary>
    public static class MatchEngineHealthCheckExtensions
    {
        /// <summary>
        /// 添加撮合引擎健康检查（无数据库依赖）
        /// </summary>
        public static IServiceCollection AddMatchEngineHealthChecks(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var healthChecksBuilder = services.AddHealthChecks();

            // 只添加 Redis 健康检查
            healthChecksBuilder
                .AddCheck<RedisHealthCheck>(
                    "redis",
                    tags: new[] { "cache", "redis", "ready" });

            // 添加撮合引擎队列健康检查
            healthChecksBuilder
                .AddCheck<MatchEngineHealthCheck>(
                    "match_engine",
                    tags: new[] { "match_engine", "queue", "ready" });

            return services;
        }
    }
}
