using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CryptoSpot.Application.Common.Models;
using CryptoSpot.Application.DTOs.Users;

namespace CryptoSpot.API.Controllers
{
    /// <summary>
    /// 认证控制器
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly ILogger<AuthController> _logger;

        public AuthController(ILogger<AuthController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 用户注册
        /// </summary>
        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody] RegisterRequestDto request)
        {
            _logger.LogWarning("Register endpoint not implemented yet");
            return StatusCode(501, new { error = "认证服务尚未实现" });
        }

        /// <summary>
        /// 用户登录
        /// </summary>
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
        {
            _logger.LogWarning("Login endpoint not implemented yet");
            return StatusCode(501, new { error = "认证服务尚未实现" });
        }

        /// <summary>
        /// 获取当前用户信息
        /// </summary>
        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> GetCurrentUser()
        {
            _logger.LogWarning("GetCurrentUser endpoint not implemented yet");
            return StatusCode(501, new { error = "认证服务尚未实现" });
        }
    }
}