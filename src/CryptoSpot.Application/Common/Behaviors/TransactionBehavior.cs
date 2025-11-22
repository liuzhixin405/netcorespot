using CryptoSpot.Bus.Core;
using CryptoSpot.Application.Abstractions.Repositories;

namespace CryptoSpot.Application.Common.Behaviors
{
    /// <summary>
    /// 事务行为（管道）- 自动保存 UnitOfWork
    /// </summary>
    public class TransactionBehavior<TCommand, TResult> : ICommandPipelineBehavior<TCommand, TResult>
        where TCommand : ICommand<TResult>
    {
        private readonly IUnitOfWork _unitOfWork;

        public TransactionBehavior(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<TResult> Handle(TCommand command, Func<TCommand, Task<TResult>> next, CancellationToken ct)
        {
            // 对于查询命令，不需要事务
            if (IsQueryCommand(command))
            {
                return await next(command);
            }

            // 命令处理在事务中执行
            var transaction = await _unitOfWork.BeginTransactionAsync();
            try
            {
                var result = await next(command);
                await _unitOfWork.CommitTransactionAsync(transaction);
                return result;
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(transaction);
                throw;
            }
        }

        private bool IsQueryCommand(TCommand command)
        {
            // 根据命令类型判断是否为查询
            var commandType = command.GetType();
            return commandType.Name.StartsWith("Get") || commandType.Name.StartsWith("Query");
        }
    }
}
