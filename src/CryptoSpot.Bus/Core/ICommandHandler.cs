using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoSpot.Bus.Core
{
    public interface ICommandHandler<TCommand,TResult>where TCommand:ICommand<TResult>
    {
        Task<TResult> HandleAsync(TCommand command,CancellationToken ct =default);
    }
}
