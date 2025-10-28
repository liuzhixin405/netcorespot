using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;
using CryptoSpot.Infrastructure.Services;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Application.Mapping;
using CryptoSpot.Domain.Entities;
using System.Text.Json;
using System.Linq;

namespace CryptoSpot.Infrastructure.Services
{
    /// <summary>
    /// 定时将缓存脏数据写回数据库的后台服务（简化实现）
    /// - 每隔一定间隔扫描 `dirty:users` 集合，读取缓存中的用户数据并落库
    /// </summary>
    public class CacheFlushHostedService : BackgroundService
    {
    private readonly RedisCacheService _cacheService;
    private readonly ILogger<CacheFlushHostedService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(30);
    private const int BATCH_SIZE = 200; // 每次处理上限
    private readonly IServiceScopeFactory _scopeFactory;
    private const string FLUSH_LOCK_KEY = "lock:cache-flush";

        public CacheFlushHostedService(RedisCacheService cacheService, ILogger<CacheFlushHostedService> logger, IServiceScopeFactory scopeFactory)
        {
            _cacheService = cacheService;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("CacheFlushHostedService started");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // 获取 dirty set
                    if (_cacheService == null)
                    {
                        await Task.Delay(_interval, stoppingToken);
                        continue;
                    }

                    var conn = _cacheService.GetType().GetProperty("_redisCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(_cacheService);
                    // 直接使用 Redis 连接去读取 set
                    // 使用分布式锁避免多副本并发刷写
                    var redisCacheField = _cacheService.GetType().GetProperty("_redisCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var redisCache = redisCacheField?.GetValue(_cacheService) as CryptoSpot.Redis.IRedisCache;
                    if (redisCache?.Connection != null)
                    {
                        var db = redisCache.Connection.GetDatabase();
                            // 尝试获取锁 (使用唯一 token)
                            var lockToken = Guid.NewGuid().ToString();
                            var lockTaken = await db.LockTakeAsync(FLUSH_LOCK_KEY, lockToken, TimeSpan.FromSeconds(25));
                            if (!lockTaken)
                            {
                                _logger.LogInformation("另一个实例正在执行 cache flush，跳过本次周期");
                            }
                            else
                            {
                                try
                                {
                                    // 批量处理 users（限制数量）
                                    var userSetKey = "dirty:users";
                                    var userMembers = (await db.SetMembersAsync(userSetKey)).Select(x => x.ToString()).Where(s => !string.IsNullOrEmpty(s)).Take(BATCH_SIZE).ToList();
                                    foreach (var m in userMembers)
                                    {
                                        if (int.TryParse(m, out int userId))
                                        {
                                            try
                                            {
                                                var userJson = await _cacheService.GetAsync<string>($"cache:user:{userId}");
                                                if (!string.IsNullOrEmpty(userJson))
                                                {
                                                    var user = JsonSerializer.Deserialize<User>(userJson);
                                                    if (user != null)
                                                    {
                                                        using var scope = _scopeFactory.CreateScope();
                                                        var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                                                        await repo.UpdateAsync(user);
                                                        _logger.LogInformation("Flushed user to DB: {UserId}", userId);
                                                    }
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                _logger.LogError(ex, "Failed flushing user {UserId}", userId);
                                            }
                                            finally
                                            {
                                                await db.SetRemoveAsync(userSetKey, m);
                                            }
                                        }
                                    }

                                    // 批量处理 kline（限制数量）
                                    var klineSetKey = "dirty:kline";
                                    var klineMembers = (await db.SetMembersAsync(klineSetKey)).Select(x => x.ToString()).Where(s => !string.IsNullOrEmpty(s)).Take(BATCH_SIZE).ToList();
                                    foreach (var item in klineMembers)
                                    {
                                        var s = item.ToString();
                                        if (string.IsNullOrEmpty(s)) continue;
                                        var parts = s.Split(':');
                                        if (parts.Length != 2) continue;
                                        var symbol = parts[0];
                                        var timeframe = parts[1];
                                        try
                                        {
                                            var klineJson = await _cacheService.GetAsync<string>($"cache:kline:{symbol}:{timeframe}");
                                            if (!string.IsNullOrEmpty(klineJson))
                                            {
                                                var klines = JsonSerializer.Deserialize<List<CryptoSpot.Domain.Entities.KLineData>>(klineJson);
                                                if (klines != null && klines.Count > 0)
                                                {
                                                    using var scope = _scopeFactory.CreateScope();
                                                    var repo = scope.ServiceProvider.GetRequiredService<CryptoSpot.Application.Abstractions.Repositories.IKLineDataRepository>();
                                                    foreach (var k in klines)
                                                    {
                                                        try
                                                        {
                                                            await repo.UpsertKLineDataAsync(k);
                                                        }
                                                        catch (Exception ex)
                                                        {
                                                            _logger.LogWarning(ex, "Upsert single kline failed {Symbol} {OpenTime}", symbol, k.OpenTime);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogError(ex, "Failed flushing kline {Symbol}:{TimeFrame}", symbol, timeframe);
                                        }
                                        finally
                                        {
                                            await db.SetRemoveAsync(klineSetKey, item);
                                        }
                                    }
                                }
                                finally
                                {
                                    // 只释放我们持有的 token
                                    await db.LockReleaseAsync(FLUSH_LOCK_KEY, lockToken);
                                }
                            }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Cache flush loop error");
                }

                await Task.Delay(_interval, stoppingToken);
            }
            _logger.LogInformation("CacheFlushHostedService stopping");
        }
    }
}
