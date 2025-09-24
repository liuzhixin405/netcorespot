using CryptoSpot.Core.Interfaces.Repositories;
using CryptoSpot.Core.Interfaces.Trading;
using CryptoSpot.Core.Entities;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Application.Services
{
    /// <summary>
    /// 交易应用服务 - 协调交易相关的用例
    /// </summary>
    public class TradingApplicationService
    {
        private readonly IOrderRepository _orderRepository;
        private readonly ITradeRepository _tradeRepository;
        private readonly ITradingPairRepository _tradingPairRepository;
        private readonly IAssetRepository _assetRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<TradingApplicationService> _logger;

        public TradingApplicationService(
            IOrderRepository orderRepository,
            ITradeRepository tradeRepository,
            ITradingPairRepository tradingPairRepository,
            IAssetRepository assetRepository,
            IUnitOfWork unitOfWork,
            ILogger<TradingApplicationService> logger)
        {
            _orderRepository = orderRepository;
            _tradeRepository = tradeRepository;
            _tradingPairRepository = tradingPairRepository;
            _assetRepository = assetRepository;
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        /// <summary>
        /// 提交订单用例
        /// </summary>
        public async Task<Order?> SubmitOrderAsync(int userId, SubmitOrderRequest request)
        {
            // 1. 验证交易对
            var tradingPair = await _tradingPairRepository.GetBySymbolAsync(request.Symbol);
            if (tradingPair == null)
            {
                throw new ArgumentException($"交易对 {request.Symbol} 不存在");
            }

            // 2. 验证用户资产
            await ValidateUserAssetsAsync(userId, request);

            // 3. 创建订单
            var order = CreateOrder(userId, tradingPair.Id, request);

            // 4. 保存订单
            var transaction = await _unitOfWork.BeginTransactionAsync();
            try
            {
                var createdOrder = await _orderRepository.AddAsync(order);
                // 保存到数据库
                await _unitOfWork.SaveChangesAsync();
                await _unitOfWork.CommitTransactionAsync(transaction);
                
                _logger.LogInformation("订单提交成功: {OrderId}, 用户: {UserId}", order.OrderId, userId);
                return createdOrder;
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(transaction);
                _logger.LogError(ex, "订单提交失败: {OrderId}", order.OrderId);
                throw;
            }
        }

        /// <summary>
        /// 取消订单用例
        /// </summary>
        public async Task<bool> CancelOrderAsync(int userId, int orderId)
        {
            var order = await _orderRepository.GetByIdAsync(orderId);
            if (order == null || order.UserId != userId)
            {
                return false;
            }

            if (order.Status != OrderStatus.Pending)
            {
                return false;
            }

            var transaction = await _unitOfWork.BeginTransactionAsync();
            try
            {
                order.Status = OrderStatus.Cancelled;
                order.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await _orderRepository.UpdateAsync(order);
                // 保存取消状态
                await _unitOfWork.SaveChangesAsync();
                await _unitOfWork.CommitTransactionAsync(transaction);
                
                _logger.LogInformation("订单取消成功: {OrderId}", order.OrderId);
                return true;
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(transaction);
                _logger.LogError(ex, "订单取消失败: {OrderId}", order.OrderId);
                throw;
            }
        }

        /// <summary>
        /// 获取用户订单历史用例
        /// </summary>
        public async Task<IEnumerable<Order>> GetUserOrderHistoryAsync(int userId, string? symbol = null, int limit = 100)
        {
            return await _orderRepository.GetUserOrderHistoryAsync(userId, symbol, limit);
        }

        /// <summary>
        /// 获取用户交易历史用例
        /// </summary>
        public async Task<IEnumerable<Trade>> GetUserTradeHistoryAsync(int userId, string? symbol = null, int limit = 100)
        {
            return await _tradeRepository.GetTradesByUserIdAsync(userId, symbol, limit);
        }

        private async Task ValidateUserAssetsAsync(int userId, SubmitOrderRequest request)
        {
            if (request.Side == OrderSide.Buy)
            {
                // 买单需要验证计价资产（如USDT）
                var quoteAsset = await _assetRepository.GetUserAssetAsync(userId, "USDT");
                if (quoteAsset == null || quoteAsset.Available < request.Price * request.Quantity)
                {
                    throw new InvalidOperationException("计价资产余额不足");
                }
            }
            else
            {
                // 卖单需要验证基础资产
                var baseAsset = await _assetRepository.GetUserAssetAsync(userId, request.Symbol.Split("USDT")[0]);
                if (baseAsset == null || baseAsset.Available < request.Quantity)
                {
                    throw new InvalidOperationException("基础资产余额不足");
                }
            }
        }

        private Order CreateOrder(int userId, int tradingPairId, SubmitOrderRequest request)
        {
            return new Order
            {
                UserId = userId,
                TradingPairId = tradingPairId,
                OrderId = GenerateOrderId(),
                ClientOrderId = request.ClientOrderId ?? GenerateOrderId(),
                Side = request.Side,
                Type = request.Type,
                Quantity = request.Quantity,
                Price = request.Price,
                Status = OrderStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        private string GenerateOrderId()
        {
            return $"ORD_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Random.Shared.Next(1000, 9999)}";
        }
    }

}
