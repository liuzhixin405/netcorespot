using CryptoSpot.Core.Commands.Trading;
using CryptoSpot.Core.Interfaces.Trading;
using CryptoSpot.Core.Events;
using CryptoSpot.Core.Events.Trading;
using CryptoSpot.Bus.Core;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Application.CommandHandlers.Trading
{
    /// <summary>
    /// 更新价格命令处理器 - 专门处理高频价格更新
    /// </summary>
    public class UpdatePriceCommandHandler : ICommandHandler<UpdatePriceCommand, UpdatePriceResult>
    {
        private readonly ITradingPairService _tradingPairService;
        private readonly IDomainEventPublisher _eventPublisher;
        private readonly ILogger<UpdatePriceCommandHandler> _logger;

        public UpdatePriceCommandHandler(
            ITradingPairService tradingPairService,
            IDomainEventPublisher eventPublisher,
            ILogger<UpdatePriceCommandHandler> logger)
        {
            _tradingPairService = tradingPairService;
            _eventPublisher = eventPublisher;
            _logger = logger;
        }

        public async Task<UpdatePriceResult> HandleAsync(UpdatePriceCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                // 验证价格数据
                if (command.Price <= 0)
                {
                    return new UpdatePriceResult
                    {
                        Success = false,
                        ErrorMessage = "价格必须大于0"
                    };
                }

                if (string.IsNullOrWhiteSpace(command.Symbol))
                {
                    return new UpdatePriceResult
                    {
                        Success = false,
                        ErrorMessage = "交易对符号不能为空"
                    };
                }

                // 更新价格
                await _tradingPairService.UpdatePriceAsync(
                    command.Symbol,
                    command.Price,
                    command.Change24h,
                    command.Volume24h,
                    command.High24h,
                    command.Low24h);

                // 发布价格更新事件
                var priceUpdatedEvent = new PriceUpdatedEvent(
                    command.Symbol,
                    command.Price,
                    command.Change24h,
                    command.Volume24h,
                    command.High24h,
                    command.Low24h);

                await _eventPublisher.PublishAsync(priceUpdatedEvent);

                _logger.LogDebug("Price updated for {Symbol}: {Price}", command.Symbol, command.Price);

                return new UpdatePriceResult
                {
                    Success = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing UpdatePriceCommand for {Symbol}", command.Symbol);
                return new UpdatePriceResult
                {
                    Success = false,
                    ErrorMessage = "价格更新失败"
                };
            }
        }
    }
}
