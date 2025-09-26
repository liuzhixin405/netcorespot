using CryptoSpot.Domain.Entities;
using CryptoSpot.Bus.Core;

namespace CryptoSpot.Core.Commands.Trading
{
    /// <summary>
    /// 处理K线数据命令 - 用于高频K线数据处理
    /// </summary>
    public class ProcessKLineDataCommand : ICommand<ProcessKLineDataResult>
    {
        public string Symbol { get; set; } = string.Empty;
        public string TimeFrame { get; set; } = string.Empty;
        public KLineData KLineData { get; set; } = null!;
        public bool IsNewKLine { get; set; }
    }

}
