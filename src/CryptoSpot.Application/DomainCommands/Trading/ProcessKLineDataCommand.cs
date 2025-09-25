using CryptoSpot.Bus.Core;
using CryptoSpot.Core.Entities;

namespace CryptoSpot.Application.DomainCommands.Trading
{
    public class ProcessKLineDataCommand : ICommand<ProcessKLineDataResult>
    {
        public string Symbol { get; set; } = string.Empty;
        public string TimeFrame { get; set; } = string.Empty;
        public KLineData KLineData { get; set; } = null!;
        public bool IsNewKLine { get; set; }
    }
}
