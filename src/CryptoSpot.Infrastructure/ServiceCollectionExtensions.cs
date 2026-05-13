using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Application.Abstractions.Services.Auth;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.Abstractions.Services.RealTime;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.Abstractions.Services.Users;
using CryptoSpot.Application.Common.Interfaces;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Infrastructure.Identity;
using CryptoSpot.Infrastructure.Services;
using CryptoSpot.Infrastructure.MatchEngine.Services;
using CryptoSpot.Infrastructure.BackgroundServices;
using CryptoSpot.Infrastructure.ExternalServices;
using CryptoSpot.Persistence.Data;
using CryptoSpot.Persistence.Repositories;
using CryptoSpot.Infrastructure.MatchEngine.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CryptoSpot.Infrastructure
{
    /// <summary>
    /// Infrastructure 层统一的服务注册扩展方法
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 注册所有 Infrastructure 层服务（持久化、Identity、后台服务等）
        /// </summary>
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            // 持久化服务
            services.AddPersistenceServices(configuration);
            
            // Identity 服务
            services.AddIdentityServices();
            
            // 后台服务
            services.AddBackgroundServices();
            
            return services;
        }

        /// <summary>
        /// 注册持久化相关服务（DbContext、仓储、UoW）
        /// </summary>
        public static IServiceCollection AddPersistenceServices(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            
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
            
            // Pooled DbContext factory（后台服务用）
            services.AddPooledDbContextFactory<ApplicationDbContext>(configureDbContext, poolSize: 128);

            // Scoped DbContext（Repository、UnitOfWork 直接注入，同一请求共享实例）
            services.AddScoped<ApplicationDbContext>(sp =>
                sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());

            // Repositories & UoW
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<ITradingPairRepository, TradingPairRepository>();
            services.AddScoped<ITradeRepository, TradeRepository>();
            services.AddScoped<IOrderRepository, OrderRepository>();
            services.AddScoped<IAssetRepository, AssetRepository>();
            services.AddScoped<IKLineDataRepository, KLineDataRepository>();
            services.AddScoped<IUnitOfWork, UnitOfWork>();

            // 应用服务
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<ITradingPairService, TradingPairService>();
            services.AddScoped<IKLineDataService, KLineDataService>();
            services.AddScoped<IOrderService, OrderService>();
            services.AddScoped<ITradeService, TradeService>();
            services.AddScoped<ITradingService, TradingService>();
            services.AddScoped<IAssetService, AssetService>();
            services.AddScoped<IPriceDataService, PriceDataService>();
            services.AddScoped<DataInitializationService>();
            services.Configure<MarketMakerOptions>(configuration.GetSection("MarketMakers"));
            services.AddSingleton<IMarketMakerRegistry, MarketMakerRegistry>();
            
            // 自动交易服务：使用单例，因为内部自己通过 IServiceScopeFactory 创建短生命周期 scope
            services.AddSingleton<IAutoTradingService, AutoTradingLogicService>();

            // 撮合引擎服务
            services.AddSingleton<InMemoryAssetStore>();
            services.AddSingleton<ITradingPairParser, TradingPairParserService>();
            services.AddSingleton<IMatchingAlgorithm, PriceTimePriorityMatchingAlgorithm>();
            services.AddSingleton<IMatchEngineService, ChannelMatchEngineService>();
            services.AddScoped<IOrderMatchingEngine, MatchEngineAdapter>();
            
            return services;
        }

        /// <summary>
        /// 注册 Identity 服务（用户认证、Token、密码哈希）
        /// </summary>
        public static IServiceCollection AddIdentityServices(this IServiceCollection services)
        {
            services.AddScoped<ICurrentUserService, CurrentUserService>();
            services.AddScoped<ITokenService, JwtTokenService>();
            services.AddScoped<IPasswordHasher, PasswordHasher>();
            services.AddSingleton(TimeProvider.System);
            return services;
        }

        /// <summary>
        /// 注册后台服务
        /// </summary>
        public static IServiceCollection AddBackgroundServices(this IServiceCollection services)
        {
            // 价格更新服务
            services.AddSingleton<PriceUpdateBatchService>();
            services.AddHostedService(sp => sp.GetRequiredService<PriceUpdateBatchService>());
            
            // SignalR 数据推送服务（不是后台服务，是普通服务）
            services.AddSingleton<IRealTimeDataPushService, SignalRDataPushService>();
            
            // 市场数据流提供者（OKX WebSocket）
            services.AddSingleton<IMarketDataStreamProvider, OkxMarketDataStreamProvider>();
            
            // 市场数据流服务（OKX WebSocket -> SignalR 推送）
            services.AddHostedService<MarketDataStreamService>();
            
            // 自动交易服务（做市商）
            services.AddHostedService<AutoTradingService>();
            
            return services;
        }

        /// <summary>
        /// 初始化数据库
        /// </summary>
        public static async Task InitDbContext(this IServiceProvider serviceProvider)
        {
            using var dbScope = serviceProvider.CreateScope();
            var context = dbScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            try
            {
                var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
                var pendingMigrations = (await context.Database.GetPendingMigrationsAsync()).ToList();

                if (pendingMigrations.Any())
                {
                    Console.WriteLine($"Applying {pendingMigrations.Count} pending migrations...");
                    await context.Database.MigrateAsync();
                    Console.WriteLine("Database migration completed successfully");
                }
                else
                {
                    Console.WriteLine("No pending migrations");
                }

                var userCount = await context.Users.CountAsync();
                Console.WriteLine($"Current user count: {userCount}");
            }
            catch (Exception ex) when (ex.Message.Contains("Duplicate key", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Migration skipped (indexes already exist): {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database setup warning (app will continue): {ex.Message}");
            }

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
