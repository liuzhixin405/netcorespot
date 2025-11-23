using FluentValidation;
using CryptoSpot.Application.DTOs.Trading;

namespace CryptoSpot.Application.Validators.Trading
{
    /// <summary>
    /// 创建订单请求验证器
    /// </summary>
    public class CreateOrderRequestDtoValidator : AbstractValidator<CreateOrderRequestDto>
    {
        public CreateOrderRequestDtoValidator()
        {
            RuleFor(x => x.Symbol)
                .NotEmpty().WithMessage("交易对符号不能为空")
                .MaximumLength(20).WithMessage("交易对符号长度不能超过20个字符")
                .Matches(@"^[A-Z0-9_/-]+$").WithMessage("交易对符号格式无效");

            RuleFor(x => x.Side)
                .IsInEnum().WithMessage("订单方向无效");

            RuleFor(x => x.Type)
                .IsInEnum().WithMessage("订单类型无效");

            RuleFor(x => x.Quantity)
                .GreaterThan(0).WithMessage("数量必须大于0")
                .LessThanOrEqualTo(1000000).WithMessage("单笔订单数量不能超过1,000,000");

            // 价格验证
            RuleFor(x => x.Price)
                .GreaterThan(0).WithMessage("价格必须大于0")
                .When(x => x.Price.HasValue);
        }
    }

}
