using CryptoSpot.Application.DomainCommands.Trading; 
using CryptoSpot.Application.Abstractions.Repositories; 
using CryptoSpot.Bus.Core;
using CryptoSpot.Domain.Entities;
using Microsoft.Extensions.Logging;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.Abstractions.Services.Users;

namespace CryptoSpot.Application.CommandHandlers.Trading
{
    /// <summary>
    /// 提交订单命令处理器
    /// </summary>
    public class SubmitOrderCommandHandler : ICommandHandler<SubmitOrderCommand, SubmitOrderResult>
    {
        private readonly ITradingPairService _tradingPairService;
        private readonly IUserRepository _userRepository;
        private readonly IOrderService _orderService;
        private readonly IOrderMatchingEngine _orderMatchingEngine;
        private readonly ICommandBus _commandBus;
        private readonly ILogger<SubmitOrderCommandHandler> _logger;
        private readonly IAssetService _assetService;
        private readonly IMarketMakerRegistry _marketMakerRegistry;

        public SubmitOrderCommandHandler(
            ITradingPairService tradingPairService,
            IUserRepository userRepository,
            IOrderService orderService,
            IOrderMatchingEngine orderMatchingEngine,
            ICommandBus commandBus,
            ILogger<SubmitOrderCommandHandler> logger,
            IAssetService assetService,
            IMarketMakerRegistry marketMakerRegistry)
        {
            _tradingPairService = tradingPairService;
            _userRepository = userRepository;
            _orderService = orderService;
            _orderMatchingEngine = orderMatchingEngine;
            _commandBus = commandBus;
            _logger = logger;
            _assetService = assetService;
            _marketMakerRegistry = marketMakerRegistry;
        }

        public async Task<SubmitOrderResult> HandleAsync(SubmitOrderCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Processing SubmitOrderCommand for user {UserId}, symbol {Symbol}", 
                    command.UserId, command.Symbol);
                
                var user = await _userRepository.GetByIdAsync(command.UserId);
                if (user == null)
                {
                    _logger.LogWarning("User {UserId} not found", command.UserId);
                    return new SubmitOrderResult
                    {
                        Success = false,
                        ErrorMessage = "用户不存在"
                    };
                }
                
                _logger.LogDebug("User {UserId} found: {Username}", user.Id, user.Username);

                var tradingPairResp = await _tradingPairService.GetTradingPairAsync(command.Symbol);
                if (!tradingPairResp.Success || tradingPairResp.Data == null)
                {
                    return new SubmitOrderResult
                    {
                        Success = false,
                        ErrorMessage = tradingPairResp.Error ?? "交易对不存在"
                    };
                }
                var tradingPair = tradingPairResp.Data;

                command.Quantity = RoundDown(command.Quantity, tradingPair.QuantityPrecision);
                if (command.Type == OrderType.Limit && command.Price.HasValue)
                    command.Price = RoundDown(command.Price.Value, tradingPair.PricePrecision);

                if (command.Quantity <= 0 || (command.Type == OrderType.Limit && command.Price.HasValue && command.Price.Value <= 0))
                {
                    return new SubmitOrderResult { Success = false, ErrorMessage = "数量或价格精度归一后无效" };
                }

                var validationResult = ValidateOrderCommand(command);
                if (!validationResult.IsValid)
                {
                    return new SubmitOrderResult
                    {
                        Success = false,
                        ErrorMessage = validationResult.ErrorMessage ?? "参数无效"
                    };
                }

                if (!_marketMakerRegistry.IsMaker(command.UserId))
                {
                    bool freezeOk = true;
                    if (command.Type == OrderType.Limit)
                    {
                        if (command.Side == OrderSide.Buy)
                        {
                            var notional = RoundDown(command.Quantity * (command.Price ?? 0), tradingPair.PricePrecision);
                            if (notional <= 0)
                                return new SubmitOrderResult { Success = false, ErrorMessage = "冻结金额为0" };
                            freezeOk = await _assetService.FreezeAssetRawAsync(command.UserId, tradingPair.QuoteAsset, notional);
                        }
                        else if (command.Side == OrderSide.Sell)
                        {
                            freezeOk = await _assetService.FreezeAssetRawAsync(command.UserId, tradingPair.BaseAsset, command.Quantity);
                        }
                    }

                    if (!freezeOk)
                    {
                        return new SubmitOrderResult { Success = false, ErrorMessage = "余额不足或冻结失败" };
                    }
                }

                var createResp = await _orderService.CreateOrderDtoAsync(
                    command.UserId,
                    command.Symbol,
                    command.Side,
                    command.Type,
                    command.Quantity,
                    command.Price);
                if (!createResp.Success || createResp.Data == null)
                {
                    return new SubmitOrderResult { Success = false, ErrorMessage = createResp.Error ?? "订单创建失败" };
                }
                var orderDto = createResp.Data;
                // 构造 Domain Order 供撮合引擎使用（后续可改为 IOrderRawAccess）
                var order = new Order
                {
                    Id = orderDto.Id,
                    OrderId = orderDto.OrderId,
                    UserId = orderDto.UserId,
                    TradingPairId = orderDto.TradingPairId,
                    Side = command.Side,
                    Type = command.Type,
                    Quantity = orderDto.Quantity,
                    Price = orderDto.Price,
                    FilledQuantity = orderDto.FilledQuantity,
                    AveragePrice = orderDto.AveragePrice,
                    Status = orderDto.Status,
                    ClientOrderId = orderDto.ClientOrderId,
                    CreatedAt = new DateTimeOffset(orderDto.CreatedAt).ToUnixTimeMilliseconds(),
                    UpdatedAt = new DateTimeOffset(orderDto.UpdatedAt).ToUnixTimeMilliseconds(),
                };
                // RemainingQuantity 使用撮合逻辑内定义：
                order.FilledQuantity = orderDto.FilledQuantity;

                var matchResult = await _orderMatchingEngine.ProcessOrderAsync(order);

                _logger.LogInformation("Order {OrderId} submitted successfully for user {UserId}", 
                    order.OrderId, command.UserId);

                return new SubmitOrderResult
                {
                    Success = true,
                    OrderId = order.Id,
                    OrderIdString = order.OrderId,
                    ExecutedTrades = matchResult.Trades.ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing SubmitOrderCommand for user {UserId}", command.UserId);
                return new SubmitOrderResult
                {
                    Success = false,
                    ErrorMessage = "订单提交失败，请稍后重试"
                };
            }
        }

        private (bool IsValid, string? ErrorMessage) ValidateOrderCommand(SubmitOrderCommand command)
        {
            if (command.Quantity <= 0)
                return (false, "订单数量必须大于0");

            if (command.Type == OrderType.Limit && (!command.Price.HasValue || command.Price.Value <= 0))
                return (false, "限价单必须指定有效价格");

            if (string.IsNullOrWhiteSpace(command.Symbol))
                return (false, "交易对符号不能为空");

            return (true, null);
        }

        private static decimal RoundDown(decimal value, int precision)
        {
            if (precision < 0) precision = 0;
            var factor = (decimal)Math.Pow(10, precision);
            return Math.Truncate(value * factor) / factor;
        }
    }
}
