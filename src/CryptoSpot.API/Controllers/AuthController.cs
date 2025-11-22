using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CryptoSpot.Application.Features.Auth.Register;
using CryptoSpot.Application.Features.Auth.Login;
using CryptoSpot.Application.Features.Auth.GetCurrentUser;
using CryptoSpot.Bus.Core;
using CryptoSpot.Application.Common.Models;

namespace CryptoSpot.API.Controllers
{
    /// <summary>
    /// 认证控制器 - 使用 CQRS 模式
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly ICommandBus _commandBus;
        private readonly ILogger<AuthController> _logger;

        public AuthController(ICommandBus commandBus, ILogger<AuthController> logger)
        {
            _commandBus = commandBus;
            _logger = logger;
        }

        /// <summary>
        /// 用户注册
        /// </summary>
        [HttpPost("register")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(RegisterResponse), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> Register([FromBody] RegisterCommand command)
        {
            var result = await _commandBus.SendAsync<RegisterCommand, Result<RegisterResponse>>(command);
            return result.IsSuccess ? Ok(result.Value) : BadRequest(new { error = result.Error });
        }

        /// <summary>
        /// 用户登录
        /// </summary>
        [HttpPost("login")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(LoginResponse), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> Login([FromBody] LoginCommand command)
        {
            var result = await _commandBus.SendAsync<LoginCommand, Result<LoginResponse>>(command);
            return result.IsSuccess ? Ok(result.Value) : BadRequest(new { error = result.Error });
        }

        /// <summary>
        /// 获取当前用户信息
        /// </summary>
        [HttpGet("me")]
        [Authorize]
        [ProducesResponseType(typeof(CurrentUserResponse), 200)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> GetCurrentUser()
        {
            var result = await _commandBus.SendAsync<GetCurrentUserQuery, Result<CurrentUserResponse>>(new GetCurrentUserQuery());
            return result.IsSuccess ? Ok(result.Value) : Unauthorized(new { error = result.Error });
        }
    }
}