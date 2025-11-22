using CryptoSpot.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace CryptoSpot.API.Extensions
{
    /// <summary>
    /// Result 扩展方法
    /// </summary>
    public static class ResultExtensions
    {
        /// <summary>
        /// 将 Result 转换为 IActionResult
        /// </summary>
        public static IActionResult ToActionResult(this Result result)
        {
            if (result.IsSuccess)
                return new OkResult();

            return new BadRequestObjectResult(new { error = result.Error });
        }

        /// <summary>
        /// 将 Result<T> 转换为 IActionResult
        /// </summary>
        public static IActionResult ToActionResult<T>(this Result<T> result)
        {
            if (result.IsSuccess)
                return new OkObjectResult(result.Value);

            return new BadRequestObjectResult(new { error = result.Error });
        }
    }
}
