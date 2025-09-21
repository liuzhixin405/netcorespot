using CryptoSpot.Bus.Core;
using CryptoSpot.Bus.Monitoring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Bus.Implementations
{
    /// <summary>
    /// CommandBus服务定位器，根据枚举类型获取具体的CommandBus实现
    /// </summary>
    public class CommandBusServiceLocator
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<CommandBusServiceLocator> _logger;

        public CommandBusServiceLocator(IServiceProvider serviceProvider, ILogger<CommandBusServiceLocator> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>
        /// 根据枚举类型获取对应的CommandBus实现
        /// </summary>
        /// <param name="type">CommandBus类型</param>
        /// <returns>对应的CommandBus实例</returns>
        public ICommandBus GetCommandBus(CommandBusType type)
        {
            _logger.LogDebug("Getting CommandBus of type: {Type}", type);

            return type switch
            {
                CommandBusType.Standard => _serviceProvider.GetRequiredService<CommandBus>(),
                CommandBusType.Dataflow => _serviceProvider.GetRequiredService<DataflowCommandBus>(),
                CommandBusType.BatchDataflow => _serviceProvider.GetRequiredService<BatchDataflowCommandBus>(),
                CommandBusType.TypedDataflow => _serviceProvider.GetRequiredService<TypedDataflowCommandBus>(),
                CommandBusType.Monitored => _serviceProvider.GetRequiredService<MonitoredCommandBus>(),
                _ => throw new ArgumentException($"Unsupported CommandBus type: {type}", nameof(type))
            };
        }

        /// <summary>
        /// 获取所有可用的CommandBus类型
        /// </summary>
        /// <returns>可用的CommandBus类型列表</returns>
        public IEnumerable<CommandBusType> GetAvailableTypes()
        {
            return Enum.GetValues<CommandBusType>();
        }
    }
}
