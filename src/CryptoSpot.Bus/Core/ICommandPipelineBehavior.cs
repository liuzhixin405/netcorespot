using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoSpot.Bus.Core
{
    /// <summary>
    /// 完整的命令管道行为接口，支持方法执行前、执行后、参数贯穿、返回值贯穿的横切关注点
    /// </summary>
    /// <typeparam name="TCommand">命令类型</typeparam>
    /// <typeparam name="TResult">返回结果类型</typeparam>
    public interface ICommandPipelineBehavior<TCommand, TResult>
    {
        /// <summary>
        /// 处理命令管道
        /// </summary>
        /// <param name="command">命令对象</param>
        /// <param name="next">下一个管道行为或最终处理器</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>处理结果</returns>
        Task<TResult> Handle(TCommand command, Func<TCommand, Task<TResult>> next, CancellationToken ct = default);
    }

    /// <summary>
    /// 方法执行前的横切关注点接口
    /// </summary>
    /// <typeparam name="TCommand">命令类型</typeparam>
    /// <typeparam name="TResult">返回结果类型</typeparam>
    public interface IPreExecutionBehavior<TCommand, TResult>
    {
        /// <summary>
        /// 方法执行前的处理
        /// </summary>
        /// <param name="command">命令对象</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>处理后的命令对象</returns>
        Task<TCommand> BeforeExecutionAsync(TCommand command, CancellationToken ct = default);
    }

    /// <summary>
    /// 方法执行后的横切关注点接口
    /// </summary>
    /// <typeparam name="TCommand">命令类型</typeparam>
    /// <typeparam name="TResult">返回结果类型</typeparam>
    public interface IPostExecutionBehavior<TCommand, TResult>
    {
        /// <summary>
        /// 方法执行后的处理
        /// </summary>
        /// <param name="command">命令对象</param>
        /// <param name="result">执行结果</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>处理后的结果</returns>
        Task<TResult> AfterExecutionAsync(TCommand command, TResult result, CancellationToken ct = default);
    }

    /// <summary>
    /// 参数贯穿的横切关注点接口
    /// </summary>
    /// <typeparam name="TCommand">命令类型</typeparam>
    /// <typeparam name="TResult">返回结果类型</typeparam>
    public interface IParameterInterceptionBehavior<TCommand, TResult>
    {
        /// <summary>
        /// 参数贯穿处理
        /// </summary>
        /// <param name="command">命令对象</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>处理后的命令对象</returns>
        Task<TCommand> InterceptParameterAsync(TCommand command, CancellationToken ct = default);
    }

    /// <summary>
    /// 返回值贯穿的横切关注点接口
    /// </summary>
    /// <typeparam name="TCommand">命令类型</typeparam>
    /// <typeparam name="TResult">返回结果类型</typeparam>
    public interface IReturnValueInterceptionBehavior<TCommand, TResult>
    {
        /// <summary>
        /// 返回值贯穿处理
        /// </summary>
        /// <param name="command">命令对象</param>
        /// <param name="result">原始结果</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>处理后的结果</returns>
        Task<TResult> InterceptReturnValueAsync(TCommand command, TResult result, CancellationToken ct = default);
    }

    /// <summary>
    /// 异常处理的横切关注点接口
    /// </summary>
    /// <typeparam name="TCommand">命令类型</typeparam>
    /// <typeparam name="TResult">返回结果类型</typeparam>
    public interface IExceptionHandlingBehavior<TCommand, TResult>
    {
        /// <summary>
        /// 异常处理
        /// </summary>
        /// <param name="command">命令对象</param>
        /// <param name="exception">异常对象</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>处理后的结果或重新抛出异常</returns>
        Task<TResult> HandleExceptionAsync(TCommand command, Exception exception, CancellationToken ct = default);
    }
}
