using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.Abstractions.Services.Users;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Infrastructure.Services;
using CryptoSpot.Infrastructure.BgService;
using CryptoSpot.Infrastructure.BgServices;
using CryptoSpot.Persistence.Data;
using CryptoSpot.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CryptoSpot.Infrastructure
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 注册持久化相关服务。
        /// 如果配置项 Persistence:EnableBackgroundServices 设置为 false，则不会注册会在启动时访问数据库的后台 HostedService
        /// （如 RedisDataLoaderService / RedisMySqlSyncService），便于在 standalone 模式下运行 MatchEngine。
        /// </summary>
        public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            
            // 提取 DbContext 配置逻辑（避免重复代码）
            Action<DbContextOptionsBuilder> configureDbContext = options =>
            {
                options.UseMySql(connectionString, ServerVersion.Parse("8.0"), mysqlOptions =>
                {
                    mysqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorNumbersToAdd: null);
                    mysqlOptions.CommandTimeout(30);
                });
                options.EnableSensitiveDataLogging(false);
                options.EnableThreadSafetyChecks(false);
            };
            
            // DbContext 工厂（专用于后台服务、单例服务等长生命周期场景）
            services.AddPooledDbContextFactory<ApplicationDbContext>(configureDbContext, poolSize: 30);
            
            // 为 Scoped 服务注册 DbContext（从工厂获取，统一池化管理）
            services.AddScoped<ApplicationDbContext>(sp =>
            {
                var factory = sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
                return factory.CreateDbContext();
            });

            // Repositories & UoW
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<ITradingPairRepository, TradingPairRepository>();
            services.AddScoped<ITradeRepository, TradeRepository>();
            services.AddScoped<IOrderRepository, OrderRepository>();
            services.AddScoped<IAssetRepository, AssetRepository>();
            services.AddScoped<IKLineDataRepository, KLineDataRepository>();
            services.AddScoped<IUnitOfWork, UnitOfWork>();

            // 应用编排服务
            services.AddScoped<IOrderService, OrderService>();
            services.AddScoped<ITradeService, TradeService>();
            services.AddScoped<ITradingService, TradingService>();
            services.AddScoped<IAssetService, AssetService>();
            services.AddScoped<DataInitializationService>();
            services.Configure<MarketMakerOptions>(configuration.GetSection("MarketMakers"));
            services.AddSingleton<IMarketMakerRegistry, MarketMakerRegistry>();

            // 批处理服务
            services.AddSingleton<PriceUpdateBatchService>();
            services.AddHostedService(sp => sp.GetRequiredService<PriceUpdateBatchService>());
            
            return services;
        }

        public static async Task InitDbContext(this IServiceProvider serviceProvider)
        {
            var dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            
            await using (var context = await dbContextFactory.CreateDbContextAsync())
            {
                try
                {
                    context.Database.EnsureCreated();
                    Console.WriteLine("Database schema created/verified successfully");

                    var userCount = await context.Users.CountAsync();
                    Console.WriteLine($"Current user count: {userCount}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Database setup failed: {ex.Message}");
                }
            }

            // DataInitializationService 仍需要 Scope（它依赖其他 scoped 服务）
            using (var scope = serviceProvider.CreateScope())
            {
                var dataInitService = scope.ServiceProvider.GetRequiredService<DataInitializationService>();
                if (await dataInitService.NeedsInitializationAsync())
                {
                    await dataInitService.InitializeDataAsync();
                    Console.WriteLine("Data initialization completed");
                }
                else
                {
                    Console.WriteLine("Data already initialized");
                }
            }
        }


    }
}
