using CryptoSpot.Bus.Core;
using CryptoSpot.Application.Common.Models;

namespace CryptoSpot.Application.Features.Auth.Register
{
    /// <summary>
    /// 注册命令
    /// </summary>
    public record RegisterCommand(
        string Username,
        string Email,
        string Password
    ) : ICommand<Result<RegisterResponse>>;

    /// <summary>
    /// 注册响应
    /// </summary>
    public record RegisterResponse(
        long UserId,
        string Username,
        string Email,
        string Token
    );
}
