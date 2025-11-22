using CryptoSpot.Bus.Core;
using Microsoft.Extensions.Logging;

namespace CryptoSpot.Application.Common.Behaviors
{
    /// <summary>
    /// 日志记录行为（管道）
    /// </summary>
    public class LoggingBehavior<TCommand, TResult> : ICommandPipelineBehavior<TCommand, TResult>
        where TCommand : ICommand<TResult>
    {
        private readonly ILogger<LoggingBehavior<TCommand, TResult>> _logger;

        public LoggingBehavior(ILogger<LoggingBehavior<TCommand, TResult>> logger)
        {
            _logger = logger;
        }

        public async Task<TResult> Handle(TCommand command, Func<TCommand, Task<TResult>> next, CancellationToken ct)
        {
            var commandName = typeof(TCommand).Name;
            _logger.LogInformation("Handling command {CommandName}: {@Command}", commandName, command);

            try
            {
                var result = await next(command);
                _logger.LogInformation("Command {CommandName} handled successfully", commandName);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling command {CommandName}", commandName);
                throw;
            }
        }
    }
}
