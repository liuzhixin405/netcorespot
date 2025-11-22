using CryptoSpot.Bus.Core;
using CryptoSpot.Application.Common.Models;

namespace CryptoSpot.Application.Features.Auth.GetCurrentUser
{
    /// <summary>
    /// 获取当前用户查询
    /// </summary>
    public record GetCurrentUserQuery() : ICommand<Result<CurrentUserResponse>>;

    /// <summary>
    /// 当前用户响应
    /// </summary>
    public record CurrentUserResponse(
        long UserId,
        string Username,
        string Email
    );
}
