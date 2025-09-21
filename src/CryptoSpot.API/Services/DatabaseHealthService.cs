using Microsoft.EntityFrameworkCore;
using CryptoSpot.Infrastructure.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoSpot.API.Services
{
    /// <summary>
    /// 数据库健康检查服务
    /// </summary>
    public class DatabaseHealthService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<DatabaseHealthService> _logger;
        private readonly TimeSpan _healthCheckInterval = TimeSpan.FromMinutes(1);

        public DatabaseHealthService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<DatabaseHealthService> logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("数据库健康检查服务启动");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckDatabaseHealthAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "数据库健康检查失败");
                }

                await Task.Delay(_healthCheckInterval, stoppingToken);
            }

            _logger.LogInformation("数据库健康检查服务停止");
        }

        private async Task CheckDatabaseHealthAsync()
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                
                // 执行简单的查询来检查连接
                var count = await context.Users.CountAsync();
                
                _logger.LogDebug("数据库健康检查通过，用户数量: {Count}", count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "数据库健康检查失败");
                throw;
            }
        }
    }
}
