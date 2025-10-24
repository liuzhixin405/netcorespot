using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.Abstractions.Services.Users;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Infrastructure.Repositories;
using CryptoSpot.Infrastructure.Repositories.Redis;
using CryptoSpot.Infrastructure.Services;
using CryptoSpot.Infrastructure.BgService;
using CryptoSpot.Infrastructure.BgServices;
using CryptoSpot.Persistence.Data;
using CryptoSpot.Persistence.Repositories;
using CryptoSpot.Bus.Extensions; // 添加 CommandBus 扩展
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

            // 注册服务实现
            services.AddScoped<ITradingPairService, TradingPairService>();

            // 应用编排 / DTO + Raw 服务
            services.AddScoped<IOrderService, OrderService>();
            services.AddScoped<ITradeService, TradeService>();
            services.AddScoped<IKLineDataService, KLineDataService>(); // 统一 K线服务实现
            services.AddScoped<ITradingService, TradingService>();
            services.AddScoped<IAssetService, AssetService>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IMatchingOrderStore, MatchingOrderStore>();

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
            
            // CommandBus：用于处理复杂业务逻辑
            // 使用 BatchDataflowCommandBus 以支持高吞吐量批处理场景
            services.AddBatchDataflowCommandBus(
                batchSize: 50,              // 批次大小：50个命令一批
                batchTimeout: TimeSpan.FromMilliseconds(100), // 批次超时：100ms
                maxConcurrency: Environment.ProcessorCount * 2 // 最大并发
            );
            
            // 添加监控（可选）
            services.AddMetricsCollector(collectionInterval: TimeSpan.FromSeconds(5));
            
            // 注册所有 CommandHandler（自动扫描并注册）
            services.AddCommandHandlers();
            
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
        
        /// <summary>
        /// 自动注册所有 CommandHandler（扫描 Application 和 Infrastructure 程序集）
        /// </summary>
        private static IServiceCollection AddCommandHandlers(this IServiceCollection services)
        {
            // 扫描 Application 程序集中所有的 CommandHandler
            var applicationAssembly = typeof(CryptoSpot.Application.CommandHandlers.Trading.SubmitOrderCommandHandler).Assembly;
            
            // 扫描 Infrastructure 程序集中所有的 CommandHandler（包括 DataSync）
            var infrastructureAssembly = typeof(CryptoSpot.Infrastructure.CommandHandlers.DataSync.SyncOrdersCommandHandler).Assembly;
            
            var assemblies = new[] { applicationAssembly, infrastructureAssembly };
            
            foreach (var assembly in assemblies)
            {
                var handlerTypes = assembly.GetTypes()
                    .Where(t => !t.IsAbstract && !t.IsInterface)
                    .Where(t => t.GetInterfaces().Any(i => 
                        i.IsGenericType && i.GetGenericTypeDefinition() == typeof(CryptoSpot.Bus.Core.ICommandHandler<,>)))
                    .ToList();
                
                foreach (var handlerType in handlerTypes)
                {
                    var interfaceType = handlerType.GetInterfaces()
                        .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(CryptoSpot.Bus.Core.ICommandHandler<,>));
                    
                    services.AddScoped(interfaceType, handlerType);
                    
                    // 记录注册信息（便于调试）
                    Console.WriteLine($"✅ 注册 CommandHandler: {handlerType.Name}");
                }
            }
            
            return services;
        }
    }
}
