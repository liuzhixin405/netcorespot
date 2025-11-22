using CryptoSpot.Bus.Core;
using CryptoSpot.Application.Common.Models;
using CryptoSpot.Application.Common.Interfaces;
using CryptoSpot.Application.Common.Exceptions;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Application.Features.Trading.CancelOrder
{
    /// <summary>
    /// 取消订单命令处理器
    /// </summary>
    public class CancelOrderCommandHandler : ICommandHandler<CancelOrderCommand, Result<CancelOrderResponse>>
    {
        private readonly ICurrentUserService _currentUser;
        private readonly IOrderRepository _orderRepository;
        private readonly IAssetRepository _assetRepository;
        private readonly ITradingPairRepository _tradingPairRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<CancelOrderCommandHandler> _logger;

        public CancelOrderCommandHandler(
            ICurrentUserService currentUser,
            IOrderRepository orderRepository,
            IAssetRepository assetRepository,
            ITradingPairRepository tradingPairRepository,
            IUnitOfWork unitOfWork,
            ILogger<CancelOrderCommandHandler> logger)
        {
            _currentUser = currentUser;
            _orderRepository = orderRepository;
            _assetRepository = assetRepository;
            _tradingPairRepository = tradingPairRepository;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<Result<CancelOrderResponse>> HandleAsync(CancelOrderCommand command, CancellationToken ct = default)
        {
            // 1. 验证用户
            if (!_currentUser.IsAuthenticated)
                return Result<CancelOrderResponse>.Failure("User is not authenticated");

            // 2. 获取订单
            var order = await _orderRepository.GetByIdAsync((int)command.OrderId);
            if (order == null)
                return Result<CancelOrderResponse>.Failure("Order not found");

            // 3. 验证订单所有权
            if (order.UserId != _currentUser.UserId)
                return Result<CancelOrderResponse>.Failure("You don't have permission to cancel this order");

            // 4. 验证订单状态
            if (order.Status == OrderStatus.Filled)
                return Result<CancelOrderResponse>.Failure("Cannot cancel a fully filled order");

            if (order.Status == OrderStatus.Cancelled)
                return Result<CancelOrderResponse>.Failure("Order is already cancelled");

            // 5. 获取交易对信息
            var tradingPair = await _tradingPairRepository.GetByIdAsync(order.TradingPairId);
            if (tradingPair == null)
                return Result<CancelOrderResponse>.Failure("Trading pair not found");

            // 6. 计算需要解冻的金额
            string currency;
            decimal amountToUnfreeze;
            decimal remainingQuantity = order.Quantity - order.FilledQuantity;

            if (order.Side == OrderSide.Buy)
            {
                currency = tradingPair.QuoteAsset;
                amountToUnfreeze = order.Type == OrderType.Limit && order.Price.HasValue
                    ? order.Price.Value * remainingQuantity
                    : remainingQuantity;
            }
            else
            {
                currency = tradingPair.BaseAsset;
                amountToUnfreeze = remainingQuantity;
            }

            // 7. 解冻资金
            var asset = await _assetRepository.GetAssetByUserIdAndSymbolAsync((int)_currentUser.UserId, currency);
            if (asset == null)
                return Result<CancelOrderResponse>.Failure($"Asset {currency} not found");

            asset.Frozen -= amountToUnfreeze;
            asset.Available += amountToUnfreeze;
            asset.Touch();
            await _assetRepository.UpdateAsync(asset);

            // 8. 更新订单状态
            order.Status = OrderStatus.Cancelled;
            order.Touch();
            await _orderRepository.UpdateAsync(order);

            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation(
                "User {UserId} cancelled order {OrderId}, unfroze {Amount} {Currency}",
                _currentUser.UserId, order.Id, amountToUnfreeze, currency);

            return Result<CancelOrderResponse>.Success(new CancelOrderResponse(
                order.Id,
                "Order cancelled successfully"
            ));
        }
    }
}
