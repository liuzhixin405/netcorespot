using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CryptoSpot.Application.DTOs.Auth;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Application.Abstractions.Services.Auth;
using System.Security.Claims;

namespace CryptoSpot.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IAuthService authService, ILogger<AuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponseDto<AuthResultDto?>>> Login([FromBody] LoginRequest request)
        {
            var result = await _authService.LoginAsync(request);
            if (result.Success) return Ok(result);
            return Unauthorized(result);
        }

        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponseDto<AuthResultDto?>>> Register([FromBody] RegisterRequest request)
        {
            var result = await _authService.RegisterAsync(request);
            if (result.Success) return Ok(result);
            return BadRequest(result);
        }

        [HttpGet("me")]
        [Authorize]
        public async Task<ActionResult<ApiResponseDto<object?>>> GetCurrentUser()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized(ApiResponseDto<object?>.CreateError("未授权"));
            }
            var result = await _authService.GetCurrentUserAsync(userId.Value);
            if (!result.Success || result.Data == null)
                return NotFound(ApiResponseDto<object?>.CreateError("用户不存在"));
            return Ok(ApiResponseDto<object?>.CreateSuccess(new
            {
                result.Data.Id,
                result.Data.Username,
                result.Data.Email,
                result.Data.CreatedAt,
                result.Data.LastLoginAt
            }));
        }

        [HttpPost("logout")]
        [Authorize]
        public async Task<ActionResult<ApiResponseDto<bool>>> Logout()
        {
            var userId = GetCurrentUserId();
            if (userId == null)
                return Unauthorized(ApiResponseDto<bool>.CreateError("未授权"));
            var result = await _authService.LogoutAsync(userId.Value);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        private int? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) ?? User.FindFirst("nameid") ?? User.FindFirst("sub");
            return userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId) ? userId : null;
        }
    }
}