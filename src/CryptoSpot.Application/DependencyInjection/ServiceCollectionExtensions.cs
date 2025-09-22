using Microsoft.Extensions.DependencyInjection;
using CryptoSpot.Core.Events;
using CryptoSpot.Core.Interfaces.Trading;
using CryptoSpot.Application.Services;
using CryptoSpot.Application.CommandHandlers.Trading;
using CryptoSpot.Application.EventHandlers.Trading;
using CryptoSpot.Core.Commands.Trading;
using CryptoSpot.Core.Events.Trading;
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

            // 注册命令处理器
            services.AddScoped<ICommandHandler<SubmitOrderCommand, SubmitOrderResult>, SubmitOrderCommandHandler>();
            services.AddScoped<ICommandHandler<CancelOrderCommand, CancelOrderResult>, CancelOrderCommandHandler>();
            services.AddScoped<ICommandHandler<UpdatePriceCommand, UpdatePriceResult>, UpdatePriceCommandHandler>();
            services.AddScoped<ICommandHandler<ProcessKLineDataCommand, ProcessKLineDataResult>, ProcessKLineDataCommandHandler>();

            // 注册事件处理器
            services.AddScoped<TradingEventHandler>();
            services.AddScoped<IDomainEventHandler<OrderCreatedEvent>>(provider => provider.GetRequiredService<TradingEventHandler>());
            services.AddScoped<IDomainEventHandler<OrderStatusChangedEvent>>(provider => provider.GetRequiredService<TradingEventHandler>());
            services.AddScoped<IDomainEventHandler<TradeExecutedEvent>>(provider => provider.GetRequiredService<TradingEventHandler>());
            services.AddScoped<IDomainEventHandler<PriceUpdatedEvent>>(provider => provider.GetRequiredService<TradingEventHandler>());
            services.AddScoped<IDomainEventHandler<KLineDataUpdatedEvent>>(provider => provider.GetRequiredService<TradingEventHandler>());
            services.AddScoped<IDomainEventHandler<AssetBalanceChangedEvent>>(provider => provider.GetRequiredService<TradingEventHandler>());

            // 注册重构后的服务
            services.AddScoped<ITradingService, RefactoredTradingService>();

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
