using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;

namespace CryptoSpot.Infrastructure.HealthChecks
{
    /// <summary>
    /// 健康检查响应写入器
    /// </summary>
    public static class HealthCheckResponseWriter
    {
        /// <summary>
        /// 写入 JSON 格式的健康检查结果
        /// </summary>
        public static Task WriteResponse(HttpContext context, HealthReport report)
        {
            context.Response.ContentType = "application/json; charset=utf-8";

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var response = new
            {
                status = report.Status.ToString(),
                totalDuration = report.TotalDuration.TotalMilliseconds,
                checks = report.Entries.Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    duration = entry.Value.Duration.TotalMilliseconds,
                    exception = entry.Value.Exception?.Message,
                    data = entry.Value.Data
                })
            };

            return context.Response.WriteAsync(
                JsonSerializer.Serialize(response, options));
        }
    }
}
