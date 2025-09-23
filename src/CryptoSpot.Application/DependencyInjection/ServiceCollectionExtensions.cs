using Microsoft.Extensions.DependencyInjection;
using CryptoSpot.Core.Interfaces.Trading;
using CryptoSpot.Core.Interfaces.Repositories;
using CryptoSpot.Application.Services;
using CryptoSpot.Application.CommandHandlers.Trading;
using CryptoSpot.Core.Commands.Trading;
using CryptoSpot.Bus.Core;
using CryptoSpot.Bus.Extensions;
using CryptoSpot.Infrastructure.Repositories;
using CryptoSpot.Core.Entities;

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

            // 注册命令处理器 - Transient，避免DbContext作用域问题
            services.AddTransient<ICommandHandler<SubmitOrderCommand, SubmitOrderResult>, SubmitOrderCommandHandler>();
            services.AddTransient<ICommandHandler<CancelOrderCommand, CancelOrderResult>, CancelOrderCommandHandler>();
            services.AddTransient<ICommandHandler<UpdatePriceCommand, UpdatePriceResult>, UpdatePriceCommandHandler>();
            services.AddTransient<ICommandHandler<ProcessKLineDataCommand, ProcessKLineDataResult>, ProcessKLineDataCommandHandler>();

            // 事件处理已统一使用CryptoSpot.Bus，无需单独注册事件处理器

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

            // 注册仓储模式 - Scoped，与DbContext生命周期一致
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            
            // 使用泛型注册通用Repository
            services.AddScoped(typeof(IRepository<>), typeof(BaseRepository<>));

            // 注册具体仓储 - Scoped，与DbContext生命周期一致
            services.AddScoped<IOrderRepository, OrderRepository>();
            services.AddScoped<ITradeRepository, TradeRepository>();
            services.AddScoped<ITradingPairRepository, TradingPairRepository>();
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IAssetRepository, AssetRepository>();
            services.AddScoped<IKLineDataRepository, KLineDataRepository>();

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
