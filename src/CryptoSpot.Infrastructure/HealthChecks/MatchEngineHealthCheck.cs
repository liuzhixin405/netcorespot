using CryptoSpot.Redis;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.HealthChecks
{
    /// <summary>
    /// 撮合引擎健康检查
    /// </summary>
    public class MatchEngineHealthCheck : IHealthCheck
    {
        private readonly IRedisService _redisService;
        private readonly ILogger<MatchEngineHealthCheck> _logger;

        public MatchEngineHealthCheck(
            IRedisService redisService,
            ILogger<MatchEngineHealthCheck> logger)
        {
            _redisService = redisService;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // 检查撮合引擎队列长度
                var queueKey = "match_engine:pending_orders";
                var queueLength = await _redisService.Connection.GetDatabase()
                    .ListLengthAsync(queueKey);

                var data = new Dictionary<string, object>
                {
                    { "pending_orders", queueLength },
                    { "check_time", DateTime.UtcNow }
                };

                // 队列堆积超过10000认为有问题
                if (queueLength > 10000)
                {
                    _logger.LogWarning("撮合引擎队列堆积: {QueueLength}", queueLength);
                    return HealthCheckResult.Degraded(
                        $"撮合引擎队列堆积：{queueLength}",
                        data: data);
                }

                // 队列堆积超过50000认为严重
                if (queueLength > 50000)
                {
                    _logger.LogError("撮合引擎队列严重堆积: {QueueLength}", queueLength);
                    return HealthCheckResult.Unhealthy(
                        $"撮合引擎队列严重堆积：{queueLength}",
                        data: data);
                }

                _logger.LogDebug("撮合引擎健康检查通过，待处理订单: {QueueLength}", queueLength);
                return HealthCheckResult.Healthy(
                    "撮合引擎运行正常",
                    data: data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "撮合引擎健康检查失败");
                return HealthCheckResult.Unhealthy(
                    "撮合引擎健康检查失败",
                    exception: ex);
            }
        }
    }
}
