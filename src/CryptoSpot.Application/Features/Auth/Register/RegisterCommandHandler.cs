using CryptoSpot.Bus.Core;
using CryptoSpot.Application.Common.Models;
using CryptoSpot.Application.Common.Interfaces;
using CryptoSpot.Application.Common.Exceptions;
using CryptoSpot.Application.Abstractions.Repositories;
using CryptoSpot.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Application.Features.Auth.Register
{
    /// <summary>
    /// 注册命令处理器
    /// </summary>
    public class RegisterCommandHandler : ICommandHandler<RegisterCommand, Result<RegisterResponse>>
    {
        private readonly IUserRepository _userRepository;
        private readonly IAssetRepository _assetRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPasswordHasher _passwordHasher;
        private readonly ITokenService _tokenService;
        private readonly ILogger<RegisterCommandHandler> _logger;

        public RegisterCommandHandler(
            IUserRepository userRepository,
            IAssetRepository assetRepository,
            IUnitOfWork unitOfWork,
            IPasswordHasher passwordHasher,
            ITokenService tokenService,
            ILogger<RegisterCommandHandler> logger)
        {
            _userRepository = userRepository;
            _assetRepository = assetRepository;
            _unitOfWork = unitOfWork;
            _passwordHasher = passwordHasher;
            _tokenService = tokenService;
            _logger = logger;
        }

        public async Task<Result<RegisterResponse>> HandleAsync(RegisterCommand command, CancellationToken ct = default)
        {
            // 1. 验证
            if (string.IsNullOrWhiteSpace(command.Username) || command.Username.Length < 3)
                return Result<RegisterResponse>.Failure("Username must be at least 3 characters");

            if (string.IsNullOrWhiteSpace(command.Email) || !command.Email.Contains("@"))
                return Result<RegisterResponse>.Failure("Invalid email address");

            if (string.IsNullOrWhiteSpace(command.Password) || command.Password.Length < 6)
                return Result<RegisterResponse>.Failure("Password must be at least 6 characters");

            // 2. 检查用户名和邮箱是否已存在
            if (await _userRepository.UsernameExistsAsync(command.Username))
                return Result<RegisterResponse>.Failure("Username already exists");

            if (await _userRepository.EmailExistsAsync(command.Email))
                return Result<RegisterResponse>.Failure("Email already exists");

            // 3. 创建用户
            var user = new User
            {
                Username = command.Username,
                Email = command.Email,
                PasswordHash = _passwordHasher.Hash(command.Password)
            };

            await _userRepository.AddAsync(user);

            // 4. 初始化资产（USDT: 100000, BTC: 10, ETH: 50）
            var initialAssets = new[]
            {
                new Asset { UserId = user.Id, Symbol = "USDT", Available = 100000m, Frozen = 0m },
                new Asset { UserId = user.Id, Symbol = "BTC", Available = 10m, Frozen = 0m },
                new Asset { UserId = user.Id, Symbol = "ETH", Available = 50m, Frozen = 0m }
            };

            foreach (var asset in initialAssets)
            {
                await _assetRepository.AddAsync(asset);
            }

            // 5. 保存到数据库
            await _unitOfWork.SaveChangesAsync();

            // 6. 生成 JWT Token
            var token = _tokenService.GenerateToken(user.Id, user.Username);

            _logger.LogInformation("User {Username} registered successfully with ID {UserId}", user.Username, user.Id);

            return Result<RegisterResponse>.Success(new RegisterResponse(
                user.Id,
                user.Username,
                user.Email,
                token
            ));
        }
    }
}
