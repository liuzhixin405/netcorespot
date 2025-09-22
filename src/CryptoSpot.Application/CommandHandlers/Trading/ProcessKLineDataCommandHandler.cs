using CryptoSpot.Core.Commands.Trading;
using CryptoSpot.Core.Interfaces.MarketData;
using CryptoSpot.Core.Events;
using CryptoSpot.Core.Events.Trading;
using CryptoSpot.Bus.Core;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Application.CommandHandlers.Trading
{
    /// <summary>
    /// 处理K线数据命令处理器 - 专门处理高频K线数据
    /// </summary>
    public class ProcessKLineDataCommandHandler : ICommandHandler<ProcessKLineDataCommand, ProcessKLineDataResult>
    {
        private readonly IKLineDataService _klineDataService;
        private readonly IDomainEventPublisher _eventPublisher;
        private readonly ILogger<ProcessKLineDataCommandHandler> _logger;

        public ProcessKLineDataCommandHandler(
            IKLineDataService klineDataService,
            IDomainEventPublisher eventPublisher,
            ILogger<ProcessKLineDataCommandHandler> logger)
        {
            _klineDataService = klineDataService;
            _eventPublisher = eventPublisher;
            _logger = logger;
        }

        public async Task<ProcessKLineDataResult> HandleAsync(ProcessKLineDataCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                // 验证K线数据
                if (command.KLineData == null)
                {
                    return new ProcessKLineDataResult
                    {
                        Success = false,
                        ErrorMessage = "K线数据不能为空"
                    };
                }

                if (string.IsNullOrWhiteSpace(command.Symbol))
                {
                    return new ProcessKLineDataResult
                    {
                        Success = false,
                        ErrorMessage = "交易对符号不能为空"
                    };
                }

                if (string.IsNullOrWhiteSpace(command.TimeFrame))
                {
                    return new ProcessKLineDataResult
                    {
                        Success = false,
                        ErrorMessage = "时间框架不能为空"
                    };
                }

                // 保存K线数据
                await _klineDataService.SaveKLineDataAsync(command.KLineData);

                // 发布K线数据更新事件
                var klineUpdatedEvent = new KLineDataUpdatedEvent(
                    command.Symbol,
                    command.TimeFrame,
                    command.KLineData,
                    command.IsNewKLine);

                await _eventPublisher.PublishAsync(klineUpdatedEvent);

                _logger.LogDebug("KLine data processed for {Symbol} {TimeFrame}: {Timestamp}", 
                    command.Symbol, command.TimeFrame, command.KLineData.Timestamp);

                return new ProcessKLineDataResult
                {
                    Success = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing ProcessKLineDataCommand for {Symbol} {TimeFrame}", 
                    command.Symbol, command.TimeFrame);
                return new ProcessKLineDataResult
                {
                    Success = false,
                    ErrorMessage = "K线数据处理失败"
                };
            }
        }
    }
}
