using Microsoft.Extensions.DependencyInjection;
using CryptoSpot.Application.Abstractions.Services.MarketData;
using CryptoSpot.Application.Abstractions.Services.Users;
using CryptoSpot.Application.CommandHandlers.Trading;
using CryptoSpot.Application.DomainCommands.Trading;
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
            services.AddTransient<ICommandHandler<ProcessKLineDataCommand, ProcessKLineDataResult>, ProcessKLineDataCommandHandler>();
            // 注册DTO映射服务
            services.AddSingleton<IDtoMappingService, DtoMappingService>();

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
