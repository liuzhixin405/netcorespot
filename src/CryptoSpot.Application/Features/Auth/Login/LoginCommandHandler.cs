using CryptoSpot.Bus.Core;
using CryptoSpot.Application.Common.Models;
using CryptoSpot.Application.Common.Interfaces;
using CryptoSpot.Application.Abstractions.Repositories;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Application.Features.Auth.Login
{
    /// <summary>
    /// 登录命令处理器
    /// </summary>
    public class LoginCommandHandler : ICommandHandler<LoginCommand, Result<LoginResponse>>
    {
        private readonly IUserRepository _userRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly ITokenService _tokenService;
        private readonly ILogger<LoginCommandHandler> _logger;

        public LoginCommandHandler(
            IUserRepository userRepository,
            IPasswordHasher passwordHasher,
            ITokenService tokenService,
            ILogger<LoginCommandHandler> logger)
        {
            _userRepository = userRepository;
            _passwordHasher = passwordHasher;
            _tokenService = tokenService;
            _logger = logger;
        }

        public async Task<Result<LoginResponse>> HandleAsync(LoginCommand command, CancellationToken ct = default)
        {
            // 1. 验证输入
            if (string.IsNullOrWhiteSpace(command.Username))
                return Result<LoginResponse>.Failure("Username is required");

            if (string.IsNullOrWhiteSpace(command.Password))
                return Result<LoginResponse>.Failure("Password is required");

            // 2. 查找用户
            var user = await _userRepository.GetByUsernameAsync(command.Username);
            if (user == null)
                return Result<LoginResponse>.Failure("Invalid username or password");

            // 3. 检查密码哈希是否存在
            if (string.IsNullOrEmpty(user.PasswordHash))
            {
                _logger.LogWarning("User {Username} has no password hash set", user.Username);
                return Result<LoginResponse>.Failure("Account not properly configured. Please contact administrator.");
            }

            // 4. 验证密码
            if (!_passwordHasher.Verify(command.Password, user.PasswordHash))
                return Result<LoginResponse>.Failure("Invalid username or password");

            // 5. 生成 Token
            var token = _tokenService.GenerateToken(user.Id, user.Username);

            _logger.LogInformation("User {Username} logged in successfully", user.Username);

            return Result<LoginResponse>.Success(new LoginResponse(
                user.Id,
                user.Username,
                user.Email ?? string.Empty, // 确保Email不为null
                token
            ));
        }
    }
}
