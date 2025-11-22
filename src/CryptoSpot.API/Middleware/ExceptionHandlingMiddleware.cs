using CryptoSpot.Domain.Exceptions;
using System.Net;
using System.Text.Json;

namespace CryptoSpot.API.Middleware
{
    /// <summary>
    /// 全局异常处理中间件
    /// </summary>
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            var code = HttpStatusCode.InternalServerError;
            var result = string.Empty;

            switch (exception)
            {
                case DomainException domainException:
                    code = HttpStatusCode.BadRequest;
                    result = JsonSerializer.Serialize(new { error = domainException.Message });
                    break;

                default:
                    _logger.LogError(exception, "Unhandled exception occurred");
                    result = JsonSerializer.Serialize(new { error = "An error occurred while processing your request" });
                    break;
            }

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)code;

            await context.Response.WriteAsync(result);
        }
    }
}
