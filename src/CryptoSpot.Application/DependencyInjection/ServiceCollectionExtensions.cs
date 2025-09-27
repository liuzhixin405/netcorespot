using Microsoft.Extensions.DependencyInjection;
// removed: using CryptoSpot.Core.Interfaces.Trading;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.Abstractions.Services.Users;
// using CryptoSpot.Core.Interfaces.Repositories; // 不在此注册具体仓储
using CryptoSpot.Application.Services;
using CryptoSpot.Application.CommandHandlers.Trading;
using CryptoSpot.Application.DomainCommands.Trading; // 使用迁移后的命令定义
using CryptoSpot.Application.Mapping;
using CryptoSpot.Bus.Core;
using CryptoSpot.Bus.Extensions;
using CryptoSpot.Application.Abstractions.Services.Trading;

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
            services.AddTransient<ICommandHandler<ProcessKLineDataCommand, ProcessKLineDataResult>, ProcessKLineDataCommandHandler>();            // 仅应用层编排服务 (不再注册 IOrderService / ITradeService 具体实现)
            
            // 注册领域服务（核心业务逻辑）- Transient，无状态
            services.AddTransient<OrderMatchingEngine>();
            
            // 注册DTO映射服务
            services.AddSingleton<IDtoMappingService, DtoMappingService>(); // 改为 Singleton，便于在单例后台服务中使用
            
            // DTO V2 服务注册集中于此，避免在 Program.cs 分散注册
            services.AddScoped<ITradingServiceV2, TradingServiceV2>();
            services.AddScoped<IAssetServiceV2, AssetServiceV2>(); // 新增
            services.AddScoped<IUserServiceV2, UserServiceV2>();   // 新增
            services.AddScoped<IKLineDataServiceV2, KLineDataServiceV2>(); // 新增
            
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
