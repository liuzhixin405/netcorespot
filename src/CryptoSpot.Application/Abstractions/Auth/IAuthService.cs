using CryptoSpot.Domain.Entities;
using CryptoSpot.Domain.Commands.Auth;

namespace CryptoSpot.Application.Abstractions.Auth
{
    public interface IAuthService
    {
        Task<AuthResult?> LoginAsync(LoginCommand command);
        Task<AuthResult?> RegisterAsync(RegisterCommand command);
        Task<User?> GetCurrentUserAsync(int userId);
        Task<bool> ValidateTokenAsync(string token);
        Task LogoutAsync(int userId);
    }
}
