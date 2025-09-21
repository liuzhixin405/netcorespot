using System;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoSpot.Bus.Core
{
    /// <summary>
    /// 强类型的命令请求接口
    /// </summary>
    public interface ICommandRequest
    {
        /// <summary>
        /// 命令类型
        /// </summary>
        Type CommandType { get; }
        
        /// <summary>
        /// 结果类型
        /// </summary>
        Type ResultType { get; }
        
        /// <summary>
        /// 执行命令并返回结果
        /// </summary>
        Task<object> ExecuteAsync(CancellationToken cancellationToken);
        
        /// <summary>
        /// 设置执行结果
        /// </summary>
        void SetResult(object result);
        
        /// <summary>
        /// 设置异常
        /// </summary>
        void SetException(Exception exception);
    }

    /// <summary>
    /// 强类型的命令请求实现
    /// </summary>
    public class CommandRequest<TCommand, TResult> : ICommandRequest
        where TCommand : ICommand<TResult>
    {
        public TCommand Command { get; }
        public TaskCompletionSource<TResult> TaskCompletionSource { get; }
        public Type CommandType => typeof(TCommand);
        public Type ResultType => typeof(TResult);

        public CommandRequest(TCommand command)
        {
            Command = command;
            TaskCompletionSource = new TaskCompletionSource<TResult>();
        }

        public async Task<object> ExecuteAsync(CancellationToken cancellationToken)
        {
            try
            {
                var result = await TaskCompletionSource.Task.WaitAsync(cancellationToken);
                return result!;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
        }

        public void SetResult(object result)
        {
            TaskCompletionSource.SetResult((TResult)result);
        }

        public void SetException(Exception exception)
        {
            TaskCompletionSource.SetException(exception);
        }
    }
}
