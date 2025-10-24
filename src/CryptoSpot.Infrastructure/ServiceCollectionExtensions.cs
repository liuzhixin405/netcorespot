using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Application.Abstractions.Services.MarketData; // K线接口
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.Abstractions.Services.Users; // IMarketMakerRegistry
using CryptoSpot.Domain.Entities; // MarketMakerOptions
using CryptoSpot.Infrastructure.Repositories;
using CryptoSpot.Infrastructure.Repositories.Redis;
using CryptoSpot.Infrastructure.Services;
using CryptoSpot.Infrastructure.BgService;
using CryptoSpot.Infrastructure.BgServices;
using CryptoSpot.Persistence.Data;
using CryptoSpot.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoSpot.Infrastructure
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            services.AddDbContextPool<ApplicationDbContext>(options =>
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
            }, poolSize: 30);

            // Repositories & UoW
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<ITradingPairRepository, TradingPairRepository>();
            services.AddScoped<ITradeRepository, TradeRepository>();
            services.AddScoped<IOrderRepository, OrderRepository>();
            services.AddScoped<IAssetRepository, AssetRepository>();
            services.AddScoped<IKLineDataRepository, KLineDataRepository>();
            services.AddScoped<IUnitOfWork, UnitOfWork>();

            // 注册服务实现
            services.AddScoped<ITradingPairService, TradingPairService>();

            // 应用编排 / DTO + Raw 服务
            services.AddScoped<IOrderService, OrderService>();
            services.AddScoped<ITradeService, TradeService>();
            services.AddScoped<IKLineDataService, KLineDataService>(); // 统一 K线服务实现
            services.AddScoped<ITradingService, TradingService>(); // 交易编排服务
            services.AddScoped<IAssetService, AssetService>(); // 资产 DTO 服务
            services.AddScoped<IUserService, UserService>(); // 用户 DTO 服务
            // services.AddScoped<IOrderRawAccess, OrderRawAccess>(); // 已替换为撮合专用接口
            services.AddScoped<IMatchingOrderStore, MatchingOrderStore>(); // 注册撮合专用订单存取

            services.AddScoped<DataInitializationService>();
            services.Configure<MarketMakerOptions>(configuration.GetSection("MarketMakers"));
            services.AddSingleton<IMarketMakerRegistry, MarketMakerRegistry>();

            // Redis-First 架构：Redis Repository 注册
            services.AddSingleton<RedisOrderRepository>();
            services.AddSingleton<RedisAssetRepository>();
            
            // Redis-First 架构：后台服务注册
            // 1. 数据加载服务（启动时从 MySQL 加载到 Redis）
            services.AddHostedService<RedisDataLoaderService>();
            
            // Redis → MySQL 同步服务
            services.AddHostedService<RedisMySqlSyncService>();
            
            // 批处理服务
            services.AddSingleton<PriceUpdateBatchService>();
            services.AddHostedService(sp => sp.GetRequiredService<PriceUpdateBatchService>());
            
            return services;
        }

        public static async Task InitDbContext(this IServiceProvider serviceProvider)
        {
            using (var scope = serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
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
