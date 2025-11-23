using System.Net;
using System.Text.Json;
using FluentValidation;
using CryptoSpot.Application.DTOs.Common;
using CryptoSpot.Domain.Exceptions;

namespace CryptoSpot.API.Middleware
{
    /// <summary>
    /// 全局异常处理中间件 - 统一处理各种异常并返回标准格式
    /// </summary>
    public class GlobalExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger;

        public GlobalExceptionHandlingMiddleware(
            RequestDelegate next,
            ILogger<GlobalExceptionHandlingMiddleware> logger)
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
            var (statusCode, response) = exception switch
            {
                FluentValidation.ValidationException validationEx => HandleFluentValidationException(validationEx),
                Domain.Exceptions.ValidationException domainValidationEx => HandleDomainValidationException(domainValidationEx),
                NotFoundException notFoundEx => HandleNotFoundException(notFoundEx),
                UnauthorizedException unauthorizedEx => HandleUnauthorizedException(unauthorizedEx),
                BusinessException businessEx => HandleBusinessException(businessEx),
                UnauthorizedAccessException _ => HandleUnauthorizedAccess(),
                _ => HandleUnknownException(exception)
            };

            // 记录日志
            if (statusCode >= 500)
            {
                _logger.LogError(exception,
                    "Server error occurred: {Message}",
                    exception.Message);
            }
            else
            {
                _logger.LogWarning(exception,
                    "Client error occurred: {Message}",
                    exception.Message);
            }

            // 设置响应
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = statusCode;

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            await context.Response.WriteAsync(
                JsonSerializer.Serialize(response, jsonOptions));
        }

        private (int statusCode, ApiResponseDto<object> response) HandleFluentValidationException(
            FluentValidation.ValidationException exception)
        {
            var errors = exception.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).ToArray());

            return (
                (int)HttpStatusCode.BadRequest,
                ApiResponseDto<object>.CreateValidationFailure(errors, "请求验证失败")
            );
        }

        private (int statusCode, ApiResponseDto<object> response) HandleDomainValidationException(
            Domain.Exceptions.ValidationException exception)
        {
            return (
                (int)HttpStatusCode.BadRequest,
                ApiResponseDto<object>.CreateError(exception.Message, "VALIDATION_ERROR")
            );
        }

        private (int statusCode, ApiResponseDto<object> response) HandleNotFoundException(
            NotFoundException exception)
        {
            return (
                (int)HttpStatusCode.NotFound,
                ApiResponseDto<object>.CreateError(exception.Message, "NOT_FOUND")
            );
        }

        private (int statusCode, ApiResponseDto<object> response) HandleUnauthorizedException(
            UnauthorizedException exception)
        {
            return (
                (int)HttpStatusCode.Forbidden,
                ApiResponseDto<object>.CreateError(exception.Message, "UNAUTHORIZED")
            );
        }

        private (int statusCode, ApiResponseDto<object> response) HandleBusinessException(
            BusinessException exception)
        {
            return (
                (int)HttpStatusCode.BadRequest,
                ApiResponseDto<object>.CreateError(exception.Message, "BUSINESS_ERROR")
            );
        }

        private (int statusCode, ApiResponseDto<object> response) HandleUnauthorizedAccess()
        {
            return (
                (int)HttpStatusCode.Unauthorized,
                ApiResponseDto<object>.CreateError("未授权访问", "UNAUTHORIZED_ACCESS")
            );
        }

        private (int statusCode, ApiResponseDto<object> response) HandleUnknownException(
            Exception exception)
        {
            return (
                (int)HttpStatusCode.InternalServerError,
                ApiResponseDto<object>.CreateError(
                    "服务器内部错误，请稍后重试",
                    "INTERNAL_SERVER_ERROR")
            );
        }
    }

    /// <summary>
    /// 中间件扩展方法
    /// </summary>
    public static class GlobalExceptionHandlingMiddlewareExtensions
    {
        public static IApplicationBuilder UseGlobalExceptionHandling(
            this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<GlobalExceptionHandlingMiddleware>();
        }
    }
}
