using FluentValidation;
using CryptoSpot.Application.Features.Auth.Register;
using CryptoSpot.Application.Features.Auth.Login;

namespace CryptoSpot.Application.Validators.Auth
{
    /// <summary>
    /// 注册命令验证器
    /// </summary>
    public class RegisterCommandValidator : AbstractValidator<RegisterCommand>
    {
        public RegisterCommandValidator()
        {
            RuleFor(x => x.Username)
                .NotEmpty().WithMessage("用户名不能为空")
                .Length(3, 50).WithMessage("用户名长度必须在3-50个字符之间")
                .Matches(@"^[a-zA-Z0-9_]+$").WithMessage("用户名只能包含字母、数字和下划线");

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("邮箱不能为空")
                .EmailAddress().WithMessage("邮箱格式无效");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("密码不能为空")
                .MinimumLength(8).WithMessage("密码长度至少为8个字符")
                .Matches(@"[A-Z]").WithMessage("密码必须包含至少一个大写字母")
                .Matches(@"[a-z]").WithMessage("密码必须包含至少一个小写字母")
                .Matches(@"[0-9]").WithMessage("密码必须包含至少一个数字");
        }
    }

    /// <summary>
    /// 登录命令验证器
    /// </summary>
    public class LoginCommandValidator : AbstractValidator<LoginCommand>
    {
        public LoginCommandValidator()
        {
            RuleFor(x => x.Username)
                .NotEmpty().WithMessage("用户名不能为空");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("密码不能为空");
        }
    }
}
