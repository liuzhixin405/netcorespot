using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CryptoSpot.Application.UseCases.Auth;
using CryptoSpot.Application.DTOs.Auth;
using CryptoSpot.Application.Abstractions.Auth;
using System.Security.Claims;

namespace CryptoSpot.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly LoginUseCase _loginUseCase;
        private readonly RegisterUseCase _registerUseCase;
        private readonly IAuthService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            LoginUseCase loginUseCase, 
            RegisterUseCase registerUseCase,
            IAuthService authService, 
            ILogger<AuthController> logger)
        {
            _loginUseCase = loginUseCase;
            _registerUseCase = registerUseCase;
            _authService = authService;
            _logger = logger;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                _logger.LogInformation("Login attempt for user: {Username}", request.Username);
                
                var result = await _loginUseCase.ExecuteAsync(request);
                if (result != null)
                {
                    _logger.LogInformation("Login successful for user: {Username}", request.Username);
                    return Ok(new
                    {
                        success = true,
                        data = result
                    });
                }
                
                _logger.LogWarning("Login failed for user: {Username}", request.Username);
                return Unauthorized(new
                {
                    success = false,
                    message = "Invalid username or password"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login for user: {Username}", request.Username);
                return StatusCode(500, new
                {
                    success = false,
                    message = "Internal server error"
                });
            }
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            try
            {
                _logger.LogInformation("Registration attempt for user: {Username}", request.Username);
                
                var result = await _registerUseCase.ExecuteAsync(request);
                if (result != null)
                {
                    _logger.LogInformation("Registration successful for user: {Username}", request.Username);
                    return Ok(new
                    {
                        success = true,
                        data = result
                    });
                }
                
                _logger.LogWarning("Registration failed for user: {Username}", request.Username);
                return BadRequest(new
                {
                    success = false,
                    message = "Registration failed. User might already exist."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration for user: {Username}", request.Username);
                return StatusCode(500, new
                {
                    success = false,
                    message = "Internal server error"
                });
            }
        }

        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> GetCurrentUser()
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId == null)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        message = "Unauthorized"
                    });
                }

                var user = await _authService.GetCurrentUserAsync(userId.Value);
                if (user != null)
                {
                    return Ok(new
                    {
                        success = true,
                        data = new
                        {
                            id = user.Id,
                            username = user.Username,
                            email = user.Email,
                            createdAt = user.CreatedAt,
                            lastLoginAt = user.LastLoginAt
                        }
                    });
                }
                
                return NotFound(new
                {
                    success = false,
                    message = "User not found"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current user");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Internal server error"
                });
            }
        }

        [HttpPost("logout")]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId != null)
                {
                    await _authService.LogoutAsync(userId.Value);
                }
                
                return Ok(new
                {
                    success = true,
                    message = "Logged out successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Internal server error"
                });
            }
        }

        private int? GetCurrentUserId()
        {
            // 尝试多种可能的 claim 类型
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? 
                             User.FindFirst("nameid") ?? 
                             User.FindFirst("sub");
            
            if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
            {
                return userId;
            }
            
            return null;
        }
    }
}