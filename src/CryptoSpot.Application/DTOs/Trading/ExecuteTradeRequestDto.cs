namespace CryptoSpot.Application.DTOs.Trading
{
    /// <summary>
    /// 执行撮合成交请求 DTO（由撮合引擎内部或测试调用）。
    /// </summary>
    public class ExecuteTradeRequestDto
    {
        public int BuyOrderId { get; set; }
        public int SellOrderId { get; set; }
        public decimal Price { get; set; }
        public decimal Quantity { get; set; }
    }
}
