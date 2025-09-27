using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CryptoSpot.Bus.Core;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Bus.Behaviors
{
    /// <summary>
    /// 参数验证行为 - 参数贯穿处理
    /// </summary>
    /// <typeparam name="TCommand">命令类型</typeparam>
    /// <typeparam name="TResult">返回结果类型</typeparam>
    public class ParameterValidationBehavior<TCommand, TResult> : IParameterInterceptionBehavior<TCommand, TResult>
    {
        private readonly ILogger<ParameterValidationBehavior<TCommand, TResult>> _logger;

        public ParameterValidationBehavior(ILogger<ParameterValidationBehavior<TCommand, TResult>> logger)
        {
            _logger = logger;
        }

        public async Task<TCommand> InterceptParameterAsync(TCommand command, CancellationToken ct = default)
        {
            _logger.LogInformation("🔍 参数验证开始: {CommandType}", typeof(TCommand).Name);

            // 使用反射进行数据注解验证
            var validationContext = new ValidationContext(command!);
            var validationResults = new List<ValidationResult>();
            if(command is TCommand) 
            if (!Validator.TryValidateObject(command, validationContext, validationResults, true))
            {
                var errors = string.Join(", ", validationResults.Select(r => r.ErrorMessage));
                _logger.LogError("❌ 参数验证失败: {Errors}", errors);
                throw new ArgumentException($"参数验证失败: {errors}");
            }

            _logger.LogInformation("✅ 参数验证通过: {CommandType}", typeof(TCommand).Name);
            await Task.CompletedTask;
            return command;
        }
    }
}
