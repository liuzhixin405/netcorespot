using CryptoSpot.Bus.Core;
using CryptoSpot.Application.Common.Models;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Application.Common.Behaviors
{
    /// <summary>
    /// FluentValidation 验证管道行为
    /// 在命令执行前自动进行验证，验证失败时返回包含错误详情的 Result
    /// </summary>
    public class ValidationBehavior<TCommand, TResult> : ICommandPipelineBehavior<TCommand, TResult>
        where TCommand : ICommand<TResult>
    {
        private readonly IEnumerable<IValidator<TCommand>> _validators;
        private readonly ILogger<ValidationBehavior<TCommand, TResult>> _logger;

        public ValidationBehavior(
            IEnumerable<IValidator<TCommand>> validators,
            ILogger<ValidationBehavior<TCommand, TResult>> logger)
        {
            _validators = validators;
            _logger = logger;
        }

        public async Task<TResult> Handle(
            TCommand command,
            Func<TCommand, Task<TResult>> next,
            CancellationToken cancellationToken)
        {
            // 如果没有验证器，直接执行下一个管道
            if (!_validators.Any())
            {
                return await next(command);
            }

            var context = new ValidationContext<TCommand>(command);

            // 并行执行所有验证器
            var validationResults = await Task.WhenAll(
                _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

            // 收集所有验证失败的错误
            var failures = validationResults
                .SelectMany(r => r.Errors)
                .Where(f => f != null)
                .ToList();

            if (failures.Count != 0)
            {
                _logger.LogWarning(
                    "Validation failed for {CommandType}. Errors: {ErrorCount}",
                    typeof(TCommand).Name,
                    failures.Count);

                // 构建错误消息
                var errorMessage = string.Join("; ", failures.Select(f => f.ErrorMessage));

                // 如果 TResult 是 Result<T> 类型，返回失败结果
                if (typeof(TResult).IsGenericType && 
                    typeof(TResult).GetGenericTypeDefinition() == typeof(Result<>))
                {
                    var resultType = typeof(TResult).GetGenericArguments()[0];
                    var failureMethod = typeof(Result<>)
                        .MakeGenericType(resultType)
                        .GetMethod("Failure");

                    return (TResult)failureMethod!.Invoke(null, new object[] { errorMessage })!;
                }

                // 如果 TResult 是 Result 类型
                if (typeof(TResult) == typeof(Result))
                {
                    return (TResult)(object)Result.Failure(errorMessage);
                }

                // 其他类型抛出验证异常
                throw new FluentValidation.ValidationException(failures);
            }

            // 验证通过，执行下一个管道
            return await next(command);
        }
    }
}
