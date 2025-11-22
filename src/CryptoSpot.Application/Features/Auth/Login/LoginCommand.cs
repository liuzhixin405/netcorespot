using CryptoSpot.Bus.Core;
using CryptoSpot.Application.Common.Models;

namespace CryptoSpot.Application.Features.Auth.Login
{
    /// <summary>
    /// 登录命令
    /// </summary>
    public record LoginCommand(
        string Username,
        string Password
    ) : ICommand<Result<LoginResponse>>;

    /// <summary>
    /// 登录响应
    /// </summary>
    public record LoginResponse(
        long UserId,
        string Username,
        string Email,
        string Token
    );
}
