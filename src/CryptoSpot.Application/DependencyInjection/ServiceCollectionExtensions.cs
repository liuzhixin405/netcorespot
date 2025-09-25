using Microsoft.Extensions.DependencyInjection;
using CryptoSpot.Core.Interfaces.Trading;
// using CryptoSpot.Core.Interfaces.Repositories; // 不在此注册具体仓储
using CryptoSpot.Application.Services;
using CryptoSpot.Application.CommandHandlers.Trading;
using CryptoSpot.Application.DomainCommands.Trading; // 使用迁移后的命令定义
using CryptoSpot.Bus.Core;
using CryptoSpot.Bus.Extensions;

namespace CryptoSpot.Application.DependencyInjection
{
    /// <summary>
    /// 服务集合扩展 - 配置依赖注入
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加Clean Architecture服务
        /// </summary>
        public static IServiceCollection AddCleanArchitecture(this IServiceCollection services)
        {
            // 使用Bus项目的高性能Dataflow实现
            services.AddDataflowCommandBus();

            // 注册 Trading 相关命令处理器（已迁移至 Application.DomainCommands）
            services.AddTransient<ICommandHandler<SubmitOrderCommand, SubmitOrderResult>, SubmitOrderCommandHandler>();
            services.AddTransient<ICommandHandler<CancelOrderCommand, CancelOrderResult>, CancelOrderCommandHandler>();
            services.AddTransient<ICommandHandler<UpdatePriceCommand, UpdatePriceResult>, UpdatePriceCommandHandler>();
            services.AddTransient<ICommandHandler<ProcessKLineDataCommand, ProcessKLineDataResult>, ProcessKLineDataCommandHandler>();

            // 注册重构后的服务
            services.AddTransient<ITradingService, RefactoredTradingService>();
            services.AddTransient<IOrderService, RefactoredOrderService>();
            services.AddTransient<ITradeService, RefactoredTradeService>();

            // 注册应用服务（协调用例）- Transient，无状态
            services.AddTransient<TradingApplicationService>();
            services.AddTransient<UserApplicationService>();
            services.AddTransient<MarketDataApplicationService>();
            
            // 注册领域服务（核心业务逻辑）- Transient，无状态
            services.AddTransient<OrderMatchingEngine>();
            
            // 注册基础设施服务（数据访问、外部服务）
            // DatabaseCoordinator 和 CacheService 在 Infrastructure 层注册

            // 移除仓储具体注册，改由 Infrastructure 层提供扩展 AddInfrastructurePersistence
            // services.AddScoped<IUnitOfWork, UnitOfWork>();
            // services.AddScoped(typeof(IRepository<>), typeof(BaseRepository<>));
            // services.AddScoped<IOrderRepository, OrderRepository>();
            // services.AddScoped<ITradeRepository, TradeRepository>();
            // services.AddScoped<ITradingPairRepository, TradingPairRepository>();
            // services.AddScoped<IUserRepository, UserRepository>();
            // services.AddScoped<IAssetRepository, AssetRepository>();
            // services.AddScoped<IKLineDataRepository, KLineDataRepository>();

            return services;
        }

        /// <summary>
        /// 添加高频数据处理服务
        /// </summary>
        public static IServiceCollection AddHighFrequencyDataProcessing(this IServiceCollection services)
        {
            // 对于高频数据场景，可以使用BatchDataflowCommandBus
            services.AddBatchDataflowCommandBus();
            
            return services;
        }
    }
}
