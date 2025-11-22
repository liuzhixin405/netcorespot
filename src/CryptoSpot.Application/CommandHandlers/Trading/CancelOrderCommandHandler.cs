using CryptoSpot.Application.DomainCommands.Trading; // 新命名空间
using CryptoSpot.Bus.Core;
using Microsoft.Extensions.Logging;
using CryptoSpot.Application.Abstractions.Services.Trading; // DTO 服务接口
using CryptoSpot.Domain.Entities; // 为枚举保留 (后续可用 DTO 中的枚举映射)

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

                // 使用 DTO 接口获取订单
                var orderResp = await _orderService.GetOrderByIdDtoAsync(command.OrderId, command.UserId);
                if (!orderResp.Success || orderResp.Data == null)
                {
                    _logger.LogWarning("Order {OrderId} not found or no permission for user {UserId}. Error: {Error}", command.OrderId, command.UserId, orderResp.Error);
                    return CancelOrderResult.CreateFailure(orderResp.Error ?? "订单不存在或无权限");
                }

                var orderDto = orderResp.Data;

                // 检查订单状态是否可以取消 (保持原规则：仅 Pending 可取消；如需放开 Active/PartiallyFilled 可调整)
                if (orderDto.Status != OrderStatus.Pending)
                {
                    _logger.LogWarning("Order {OrderId} cannot be cancelled, current status: {Status}",
                        command.OrderId, orderDto.Status);
                    return CancelOrderResult.CreateFailure("订单状态不允许取消");
                }

                // 执行取消操作 (DTO 接口)
                var cancelResp = await _orderService.CancelOrderDtoAsync(command.OrderId, command.UserId);
                if (cancelResp.Success && cancelResp.Data)
                {
                    _logger.LogInformation("Successfully cancelled order {OrderId} for user {UserId}",
                        command.OrderId, command.UserId);
                    return CancelOrderResult.CreateSuccess();
                }
                else
                {
                    _logger.LogError("Failed to cancel order {OrderId} for user {UserId}. Error: {Error}",
                        command.OrderId, command.UserId, cancelResp.Error);
                    return CancelOrderResult.CreateFailure(cancelResp.Error ?? "取消订单失败");
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
