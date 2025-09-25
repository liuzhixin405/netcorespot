using CryptoSpot.Application.DomainCommands.Trading; // 替换 Core.Commands.Trading
using CryptoSpot.Core.Interfaces.Trading;
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
        private readonly ICommandBus _commandBus;
        private readonly ILogger<UpdatePriceCommandHandler> _logger;

        public UpdatePriceCommandHandler(
            ITradingPairService tradingPairService,
            ICommandBus commandBus,
            ILogger<UpdatePriceCommandHandler> logger)
        {
            _tradingPairService = tradingPairService;
            _commandBus = commandBus;
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

                // 发布价格更新事件 - 使用CommandBus发送相关命令
                // 如果需要发布事件，可以创建相应的事件命令并通过CommandBus发送
                // await _commandBus.SendAsync<PriceUpdatedEventCommand, PriceUpdatedEventResult>(eventCommand);

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
