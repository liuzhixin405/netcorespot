using CryptoSpot.Persistence.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Infrastructure.HealthChecks
{
    /// <summary>
    /// 数据库健康检查
    /// </summary>
    public class DatabaseHealthCheck : IHealthCheck
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<DatabaseHealthCheck> _logger;

        public DatabaseHealthCheck(
            ApplicationDbContext dbContext,
            ILogger<DatabaseHealthCheck> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var startTime = DateTime.UtcNow;
                
                // 执行简单查询测试连接
                var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
                
                var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

                if (!canConnect)
                {
                    _logger.LogError("无法连接到数据库");
                    return HealthCheckResult.Unhealthy("无法连接到数据库");
                }

                var data = new Dictionary<string, object>
                {
                    { "response_time_ms", responseTime },
                    { "database", _dbContext.Database.GetDbConnection().Database }
                };

                if (responseTime > 1000)
                {
                    _logger.LogWarning("数据库响应缓慢: {ResponseTime}ms", responseTime);
                    return HealthCheckResult.Degraded(
                        "数据库响应缓慢",
                        data: data);
                }

                _logger.LogDebug("数据库健康检查通过: {ResponseTime}ms", responseTime);
                return HealthCheckResult.Healthy(
                    "数据库连接正常",
                    data: data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "数据库健康检查失败");
                return HealthCheckResult.Unhealthy(
                    "数据库健康检查失败",
                    exception: ex);
            }
        }
    }
}
