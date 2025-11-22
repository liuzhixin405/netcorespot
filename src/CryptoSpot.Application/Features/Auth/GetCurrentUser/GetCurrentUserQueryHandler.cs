using CryptoSpot.Bus.Core;
using CryptoSpot.Application.Common.Models;
using CryptoSpot.Application.Common.Interfaces;
using CryptoSpot.Application.Common.Exceptions;
using CryptoSpot.Application.Abstractions.Repositories;

namespace CryptoSpot.Application.Features.Auth.GetCurrentUser
{
    /// <summary>
    /// 获取当前用户查询处理器
    /// </summary>
    public class GetCurrentUserQueryHandler : ICommandHandler<GetCurrentUserQuery, Result<CurrentUserResponse>>
    {
        private readonly ICurrentUserService _currentUser;
        private readonly IUserRepository _userRepository;

        public GetCurrentUserQueryHandler(
            ICurrentUserService currentUser,
            IUserRepository userRepository)
        {
            _currentUser = currentUser;
            _userRepository = userRepository;
        }

        public async Task<Result<CurrentUserResponse>> HandleAsync(GetCurrentUserQuery query, CancellationToken ct = default)
        {
            if (!_currentUser.IsAuthenticated)
                return Result<CurrentUserResponse>.Failure("User is not authenticated");

            var user = await _userRepository.GetByIdAsync(_currentUser.UserId);
            if (user == null)
                return Result<CurrentUserResponse>.Failure("User not found");

            return Result<CurrentUserResponse>.Success(new CurrentUserResponse(
                user.Id,
                user.Username,
                user.Email
            ));
        }
    }
}
