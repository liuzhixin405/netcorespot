using FluentValidation;
using CryptoSpot.Application.DTOs.Users;

namespace CryptoSpot.Application.Validators.Users
{
    /// <summary>
    /// 创建用户请求验证器
    /// </summary>
    public class CreateUserRequestDtoValidator : AbstractValidator<CreateUserRequestDto>
    {
        public CreateUserRequestDtoValidator()
        {
            RuleFor(x => x.Username)
                .NotEmpty().WithMessage("用户名不能为空")
                .Length(3, 50).WithMessage("用户名长度必须在3-50个字符之间")
                .Matches(@"^[a-zA-Z0-9_]+$").WithMessage("用户名只能包含字母、数字和下划线");

            RuleFor(x => x.Email)
                .EmailAddress().WithMessage("邮箱格式无效")
                .When(x => !string.IsNullOrEmpty(x.Email));

            RuleFor(x => x.Password)
                .MinimumLength(6).WithMessage("密码长度至少为6个字符")
                .When(x => !string.IsNullOrEmpty(x.Password));
        }
    }

    /// <summary>
    /// 更新用户请求验证器
    /// </summary>
    public class UpdateUserRequestDtoValidator : AbstractValidator<UpdateUserRequestDto>
    {
        public UpdateUserRequestDtoValidator()
        {
            RuleFor(x => x.Email)
                .EmailAddress().WithMessage("邮箱格式无效")
                .When(x => !string.IsNullOrEmpty(x.Email));

            RuleFor(x => x.Description)
                .MaximumLength(200).WithMessage("描述信息不能超过200个字符")
                .When(x => !string.IsNullOrEmpty(x.Description));
        }
    }

    /// <summary>
    /// 资产操作请求验证器
    /// </summary>
    public class AssetOperationRequestDtoValidator : AbstractValidator<AssetOperationRequestDto>
    {
        public AssetOperationRequestDtoValidator()
        {
            RuleFor(x => x.Symbol)
                .NotEmpty().WithMessage("资产符号不能为空");

            RuleFor(x => x.Amount)
                .GreaterThan(0).WithMessage("金额必须大于0");
        }
    }

    /// <summary>
    /// 资产转账请求验证器
    /// </summary>
    public class AssetTransferRequestDtoValidator : AbstractValidator<AssetTransferRequestDto>
    {
        public AssetTransferRequestDtoValidator()
        {
            RuleFor(x => x.ToUserId)
                .GreaterThan(0).WithMessage("接收方用户ID无效");

            RuleFor(x => x.Symbol)
                .NotEmpty().WithMessage("资产符号不能为空");

            RuleFor(x => x.Amount)
                .GreaterThan(0).WithMessage("转账金额必须大于0");
        }
    }
}
