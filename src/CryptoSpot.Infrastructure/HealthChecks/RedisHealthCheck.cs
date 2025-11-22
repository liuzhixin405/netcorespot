using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CryptoSpot.Infrastructure.HealthChecks
{
    /// <summary>
    /// Redis 健康检查
    /// </summary>
    public class RedisHealthCheck : IHealthCheck
    {
        private readonly IConnectionMultiplexer _connectionMultiplexer;
        private readonly ILogger<RedisHealthCheck> _logger;

        public RedisHealthCheck(
            IConnectionMultiplexer connectionMultiplexer,
            ILogger<RedisHealthCheck> logger)
        {
            _connectionMultiplexer = connectionMultiplexer;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // 检查连接状态
                if (!_connectionMultiplexer.IsConnected)
                {
                    _logger.LogWarning("Redis 未连接");
                    return HealthCheckResult.Unhealthy(
                        "Redis 未连接",
                        data: new Dictionary<string, object>
                        {
                            { "is_connected", false },
                            { "endpoints", string.Join(", ", _connectionMultiplexer.GetEndPoints().Select(e => e.ToString())) }
                        });
                }

                var db = _connectionMultiplexer.GetDatabase();
                
                // 设置较短的超时时间进行 Ping 测试
                var pingTime = await db.PingAsync();

                var data = new Dictionary<string, object>
                {
                    { "ping_ms", pingTime.TotalMilliseconds },
                    { "is_connected", _connectionMultiplexer.IsConnected },
                    { "connected_endpoints", _connectionMultiplexer.GetEndPoints().Length },
                    { "endpoints", string.Join(", ", _connectionMultiplexer.GetEndPoints().Select(e => e.ToString())) }
                };

                // 检查响应时间
                if (pingTime.TotalMilliseconds > 1000)
                {
                    _logger.LogWarning("Redis 响应缓慢: {PingTime}ms", pingTime.TotalMilliseconds);
                    return HealthCheckResult.Degraded(
                        $"Redis响应缓慢 ({pingTime.TotalMilliseconds:F2}ms)",
                        data: data);
                }

                _logger.LogDebug("Redis 健康检查通过: {PingTime}ms", pingTime.TotalMilliseconds);
                return HealthCheckResult.Healthy(
                    $"Redis连接正常 (ping: {pingTime.TotalMilliseconds:F2}ms)",
                    data: data);
            }
            catch (RedisConnectionException ex)
            {
                _logger.LogError(ex, "Redis 连接异常");
                return HealthCheckResult.Unhealthy(
                    "Redis连接异常: " + ex.Message,
                    exception: ex,
                    data: new Dictionary<string, object>
                    {
                        { "error_type", "connection_error" },
                        { "is_connected", _connectionMultiplexer.IsConnected }
                    });
            }
            catch (RedisTimeoutException ex)
            {
                _logger.LogError(ex, "Redis 超时");
                return HealthCheckResult.Unhealthy(
                    "Redis超时: " + ex.Message,
                    exception: ex,
                    data: new Dictionary<string, object>
                    {
                        { "error_type", "timeout" },
                        { "is_connected", _connectionMultiplexer.IsConnected }
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis 健康检查失败");
                return HealthCheckResult.Unhealthy(
                    "Redis健康检查失败: " + ex.Message,
                    exception: ex,
                    data: new Dictionary<string, object>
                    {
                        { "error_type", "unknown" },
                        { "is_connected", _connectionMultiplexer?.IsConnected ?? false }
                    });
            }
        }
    }
}
