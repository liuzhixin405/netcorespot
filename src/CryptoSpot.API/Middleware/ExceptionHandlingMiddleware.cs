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
        private readonly IHostEnvironment _environment;

        public ExceptionHandlingMiddleware(
            RequestDelegate next, 
            ILogger<ExceptionHandlingMiddleware> logger,
            IHostEnvironment environment)
        {
            _next = next;
            _logger = logger;
            _environment = environment;
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
            HttpStatusCode statusCode;
            object error;

            switch (exception)
            {
                case ValidationException validationEx:
                    statusCode = HttpStatusCode.BadRequest;
                    error = new
                    {
                        type = "validation_error",
                        message = validationEx.Message,
                        errors = validationEx.Errors
                    };
                    break;

                case NotFoundException notFoundEx:
                    statusCode = HttpStatusCode.NotFound;
                    error = new
                    {
                        type = "not_found",
                        message = notFoundEx.Message
                    };
                    break;

                case UnauthorizedException unauthorizedEx:
                    statusCode = HttpStatusCode.Unauthorized;
                    error = new
                    {
                        type = "unauthorized",
                        message = unauthorizedEx.Message
                    };
                    break;

                case BusinessException businessEx:
                    statusCode = HttpStatusCode.BadRequest;
                    error = new
                    {
                        type = "business_error",
                        message = businessEx.Message
                    };
                    break;

                case DomainException domainEx:
                    statusCode = HttpStatusCode.BadRequest;
                    error = new
                    {
                        type = "domain_error",
                        message = domainEx.Message
                    };
                    break;

                default:
                    statusCode = HttpStatusCode.InternalServerError;
                    error = new
                    {
                        type = "internal_error",
                        message = _environment.IsDevelopment()
                            ? exception.Message
                            : "处理请求时发生错误"
                    };
                    break;
            }

            // 记录错误日志
            if (statusCode == HttpStatusCode.InternalServerError)
            {
                _logger.LogError(
                    exception,
                    "未处理的异常: {ExceptionType} - {Message}",
                    exception.GetType().Name,
                    exception.Message);
            }
            else
            {
                _logger.LogWarning(
                    "业务异常: {ExceptionType} - {Message}",
                    exception.GetType().Name,
                    exception.Message);
            }

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)statusCode;

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = _environment.IsDevelopment()
            };

            // 在开发环境添加堆栈跟踪
            object response = error;
            if (_environment.IsDevelopment() && statusCode == HttpStatusCode.InternalServerError)
            {
                response = new
                {
                    error = error,
                    stackTrace = exception.StackTrace,
                    innerException = exception.InnerException?.Message
                };
            }

            var json = JsonSerializer.Serialize(response, options);
            await context.Response.WriteAsync(json);
        }
    }
}
