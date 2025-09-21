using Microsoft.AspNetCore.SignalR;
using CryptoSpot.Core.Entities;
using CryptoSpot.Core.Interfaces.MarketData;
using CryptoSpot.Core.Interfaces.Trading;

namespace CryptoSpot.API.Hubs
{
    public class TradingHub : Hub
    {
        private readonly IKLineDataService _klineDataService;
        private readonly IOrderMatchingEngine _orderMatchingEngine;
        private readonly ILogger<TradingHub> _logger;

        public TradingHub(IKLineDataService klineDataService, IOrderMatchingEngine orderMatchingEngine, ILogger<TradingHub> logger)
        {
            _klineDataService = klineDataService;
            _orderMatchingEngine = orderMatchingEngine;
            _logger = logger;
        }
        public async Task JoinGroup(string groupName)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }

        public async Task LeaveGroup(string groupName)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        }

        // 订阅K线数据
        public async Task SubscribeKLineData(string symbol, string interval)
        {
            try
            {
                var groupName = $"kline_{symbol}_{interval}";
                await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
                
                // 发送历史数据
                var historicalData = await _klineDataService.GetKLineDataAsync(symbol, interval, 100);
                if (historicalData.Any())
                {
                    var historicalKLines = historicalData.Select(k => new
                    {
                        symbol = symbol,
                        interval = interval,
                        timestamp = k.OpenTime,
                        open = k.Open,
                        high = k.High,
                        low = k.Low,
                        close = k.Close,
                        volume = k.Volume
                    }).ToList();

                    await Clients.Caller.SendAsync("HistoricalKLineData", historicalKLines);
                }
                
                // 通知客户端订阅成功
                await Clients.Caller.SendAsync("KLineSubscribed", symbol, interval);
                
                _logger.LogDebug($"Client {Context.ConnectionId} subscribed to {symbol} {interval} K-line data");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to subscribe K-line data for {symbol} {interval}");
                await Clients.Caller.SendAsync("Error", $"Failed to subscribe to {symbol} {interval}");
            }
        }

        // 取消订阅K线数据
        public async Task UnsubscribeKLineData(string symbol, string interval)
        {
            var groupName = $"kline_{symbol}_{interval}";
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            
            await Clients.Caller.SendAsync("KLineUnsubscribed", symbol, interval);
        }

        // 订阅价格数据
        public async Task SubscribePriceData(string[] symbols)
        {
            foreach (var symbol in symbols)
            {
                var groupName = $"price_{symbol}";
                await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            }
            
            await Clients.Caller.SendAsync("PriceSubscribed", symbols);
        }

        // 取消订阅价格数据
        public async Task UnsubscribePriceData(string[] symbols)
        {
            foreach (var symbol in symbols)
            {
                var groupName = $"price_{symbol}";
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            }
            
            await Clients.Caller.SendAsync("PriceUnsubscribed", symbols);
        }

        // 订阅订单簿数据
        public async Task SubscribeOrderBook(string symbol, int depth = 20)
        {
            try
            {
                var groupName = $"orderbook_{symbol}";
                await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
                
                // 发送当前订单簿数据
                var orderBookDepth = await _orderMatchingEngine.GetOrderBookDepthAsync(symbol, depth);
                if (orderBookDepth != null)
                {
                    var orderBookData = new
                    {
                        symbol = symbol,
                        bids = orderBookDepth.Bids.Select(b => new
                        {
                            price = b.Price,
                            amount = b.Quantity,
                            total = b.Total
                        }).ToList(),
                        asks = orderBookDepth.Asks.Select(a => new
                        {
                            price = a.Price,
                            amount = a.Quantity,
                            total = a.Total
                        }).ToList(),
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };

                    await Clients.Caller.SendAsync("OrderBookData", orderBookData);
                }
                
                // 通知客户端订阅成功
                await Clients.Caller.SendAsync("OrderBookSubscribed", symbol);
                
                _logger.LogDebug($"Client {Context.ConnectionId} subscribed to {symbol} order book");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to subscribe order book for {symbol}");
                await Clients.Caller.SendAsync("Error", $"Failed to subscribe to {symbol} order book");
            }
        }

        // 取消订阅订单簿数据
        public async Task UnsubscribeOrderBook(string symbol)
        {
            var groupName = $"orderbook_{symbol}";
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            
            await Clients.Caller.SendAsync("OrderBookUnsubscribed", symbol);
        }

        public override async Task OnConnectedAsync()
        {
            await Clients.Caller.SendAsync("Connected", Context.ConnectionId);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            // 客户端断开连接时的清理工作
            await base.OnDisconnectedAsync(exception);
        }
    }
}
