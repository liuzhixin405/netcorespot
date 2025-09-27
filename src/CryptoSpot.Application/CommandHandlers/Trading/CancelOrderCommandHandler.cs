using CryptoSpot.Application.DomainCommands.Trading; // 新命名空间
using CryptoSpot.Bus.Core;
using Microsoft.Extensions.Logging;
using CryptoSpot.Domain.Entities;
using CryptoSpot.Application.Abstractions.Services.Trading; // 添加引用枚举

namespace CryptoSpot.Application.CommandHandlers.Trading
{
    /// <summary>
    /// 取消订单命令处理器
    /// </summary>
    public class CancelOrderCommandHandler : ICommandHandler<CancelOrderCommand, CancelOrderResult>
    {
        private readonly IOrderService _orderService;
        private readonly ILogger<CancelOrderCommandHandler> _logger;

        public CancelOrderCommandHandler(
            IOrderService orderService,
            ILogger<CancelOrderCommandHandler> logger)
        {
            _orderService = orderService;
            _logger = logger;
        }

        public async Task<CancelOrderResult> HandleAsync(CancelOrderCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Processing cancel order command for OrderId: {OrderId}, UserId: {UserId}", 
                    command.OrderId, command.UserId);

                // 验证订单是否存在且属于该用户
                var order = await _orderService.GetOrderByIdAsync(command.OrderId, command.UserId);
                if (order == null)
                {
                    _logger.LogWarning("Order {OrderId} not found for user {UserId}", command.OrderId, command.UserId);
                    return CancelOrderResult.CreateFailure("订单不存在或无权限");
                }

                // 检查订单状态是否可以取消
                if (order.Status != OrderStatus.Pending)
                {
                    _logger.LogWarning("Order {OrderId} cannot be cancelled, current status: {Status}", 
                        command.OrderId, order.Status);
                    return CancelOrderResult.CreateFailure("订单状态不允许取消");
                }

                // 执行取消操作
                var success = await _orderService.CancelOrderAsync(command.OrderId, command.UserId);
                if (success)
                {
                    _logger.LogInformation("Successfully cancelled order {OrderId} for user {UserId}", 
                        command.OrderId, command.UserId);
                    return CancelOrderResult.CreateSuccess();
                }
                else
                {
                    _logger.LogError("Failed to cancel order {OrderId} for user {UserId}", 
                        command.OrderId, command.UserId);
                    return CancelOrderResult.CreateFailure("取消订单失败");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing cancel order command for OrderId: {OrderId}, UserId: {UserId}", 
                    command.OrderId, command.UserId);
                return CancelOrderResult.CreateFailure($"取消订单时发生错误: {ex.Message}");
            }
        }
    }
}
