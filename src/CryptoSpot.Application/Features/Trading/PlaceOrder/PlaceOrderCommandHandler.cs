using CryptoSpot.Bus.Core;
using CryptoSpot.Application.Common.Models;
using CryptoSpot.Application.Common.Interfaces;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Domain.Exceptions;
using CryptoSpot.Domain.DomainEvents;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Application.Features.Trading.PlaceOrder
{
    /// <summary>
    /// 下单命令处理器
    /// </summary>
    public class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand, Result<PlaceOrderResponse>>
    {
        private readonly ICurrentUserService _currentUser;
        private readonly IOrderRepository _orderRepository;
        private readonly IAssetRepository _assetRepository;
        private readonly ITradingPairRepository _tradingPairRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<PlaceOrderCommandHandler> _logger;

        public PlaceOrderCommandHandler(
            ICurrentUserService currentUser,
            IOrderRepository orderRepository,
            IAssetRepository assetRepository,
            ITradingPairRepository tradingPairRepository,
            IUnitOfWork unitOfWork,
            ILogger<PlaceOrderCommandHandler> logger)
        {
            _currentUser = currentUser;
            _orderRepository = orderRepository;
            _assetRepository = assetRepository;
            _tradingPairRepository = tradingPairRepository;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<Result<PlaceOrderResponse>> HandleAsync(PlaceOrderCommand command, CancellationToken ct = default)
        {
            // 1. 验证用户
            if (!_currentUser.IsAuthenticated)
                return Result<PlaceOrderResponse>.Failure("User is not authenticated");

            // 2. 验证交易对
            var tradingPair = await _tradingPairRepository.GetBySymbolAsync(command.Symbol);
            if (tradingPair == null)
                return Result<PlaceOrderResponse>.Failure($"Trading pair {command.Symbol} not found");

            if (!tradingPair.IsActive)
                return Result<PlaceOrderResponse>.Failure($"Trading pair {command.Symbol} is not active");

            // 3. 验证订单参数
            if (command.Quantity <= 0)
                return Result<PlaceOrderResponse>.Failure("Quantity must be greater than 0");

            if (command.Type == OrderType.Limit && command.Price <= 0)
                return Result<PlaceOrderResponse>.Failure("Price must be greater than 0 for limit orders");

            // 4. 检查余额并冻结资金
            string currency;
            decimal amountToFreeze;

            if (command.Side == OrderSide.Buy)
            {
                // 买单：冻结报价币种（如 BTC/USDT 中的 USDT）
                currency = tradingPair.QuoteAsset;
                amountToFreeze = command.Type == OrderType.Limit 
                    ? command.Price * command.Quantity 
                    : command.Quantity; // 市价单时 Quantity 表示金额
            }
            else
            {
                // 卖单：冻结标的币种（如 BTC/USDT 中的 BTC）
                currency = tradingPair.BaseAsset;
                amountToFreeze = command.Quantity;
            }

            var asset = await _assetRepository.GetAssetByUserIdAndSymbolAsync((int)_currentUser.UserId, currency);
            if (asset == null || asset.Available < amountToFreeze)
            {
                return Result<PlaceOrderResponse>.Failure(
                    $"Insufficient {currency} balance. Required: {amountToFreeze}, Available: {asset?.Available ?? 0}");
            }

            // 冻结资金
            asset.Available -= amountToFreeze;
            asset.Frozen += amountToFreeze;
            asset.Touch();
            await _assetRepository.UpdateAsync(asset);

            // 5. 创建订单
            var order = new Order
            {
                UserId = (int)_currentUser.UserId,
                TradingPairId = tradingPair.Id,
                OrderId = Guid.NewGuid().ToString("N"),
                Side = command.Side,
                Type = command.Type,
                Price = command.Price,
                Quantity = command.Quantity,
                FilledQuantity = 0,
                Status = OrderStatus.Pending
            };

            await _orderRepository.AddAsync(order);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation(
                "User {UserId} placed {Side} order for {Quantity} {Symbol} at {Price}",
                _currentUser.UserId, command.Side, command.Quantity, command.Symbol, command.Price);

            return Result<PlaceOrderResponse>.Success(new PlaceOrderResponse(
                order.Id,
                tradingPair.Symbol,
                order.Side,
                order.Type,
                order.Price ?? 0,
                order.Quantity,
                order.Status,
                order.CreatedDateTime
            ));
        }
    }
}
