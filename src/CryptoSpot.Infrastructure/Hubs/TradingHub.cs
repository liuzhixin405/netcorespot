using Microsoft.AspNetCore.SignalR;
using CryptoSpot.Domain.Entities;
using Microsoft.Extensions.Logging;
using CryptoSpot.Application.Abstractions.Services.Trading;
using CryptoSpot.Application.Abstractions.Repositories;   // 使用仓储接口
using CryptoSpot.Application.Abstractions.Services.MarketData;   // 交易对服务

namespace CryptoSpot.Infrastructure.Hubs
{    public class TradingHub : Hub
    {
        private readonly IKLineDataRepository _klineDataRepository;
        private readonly ITradingPairService _tradingPairService;
        private readonly IOrderMatchingEngine _orderMatchingEngine;
        private readonly ILogger<TradingHub> _logger;

        public TradingHub(IKLineDataRepository klineDataRepository, ITradingPairService tradingPairService, IOrderMatchingEngine orderMatchingEngine, ILogger<TradingHub> logger)
        {
            _klineDataRepository = klineDataRepository;
            _tradingPairService = tradingPairService;
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

        // 订阅K线数据（只订阅实时更新，不推送历史数据）
        public async Task SubscribeKLineData(string symbol, string interval)
        {
            try
            {
                var groupName = $"kline_{symbol}_{interval}";
                await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
                await Clients.Caller.SendAsync("KLineSubscribed", symbol, interval);                // 使用仓储获取最新一条实体
                var tpIdResp = await _tradingPairService.GetTradingPairIdAsync(symbol);
                var tpId = tpIdResp.Success ? tpIdResp.Data : 0;
                KLineData? latest = tpId <= 0 ? null : await _klineDataRepository.GetLatestKLineDataAsync(tpId, interval);
                if (latest != null)
                {
                    var initial = new
                    {
                        symbol,
                        interval,
                        timestamp = latest.OpenTime,
                        open = latest.Open,
                        high = latest.High,
                        low = latest.Low,
                        close = latest.Close,
                        volume = latest.Volume,
                        isNewKLine = false
                    };
                    await Clients.Caller.SendAsync("KLineUpdate", initial, false);
                    _logger.LogDebug("Initial KLine snapshot sent to {ConnId} for {Symbol} {Interval}", Context.ConnectionId, symbol, interval);
                }
                else
                {
                    _logger.LogDebug("No existing KLine found for initial push {Symbol} {Interval}", symbol, interval);
                }
                _logger.LogDebug($"Client {Context.ConnectionId} subscribed to {symbol} {interval} K-line real-time updates");
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
        public async Task SubscribeOrderBook(string symbol, int depth = 5)
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
                        type = "snapshot",
                        symbol,
                        bids = orderBookDepth.Bids.Select(b => new { price = b.Price, amount = b.Quantity, total = b.Total }).ToList(),
                        asks = orderBookDepth.Asks.Select(a => new { price = a.Price, amount = a.Quantity, total = a.Total }).ToList(),
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };
                    await Clients.Caller.SendAsync("OrderBookData", orderBookData);
                    _logger.LogInformation("Sent order book snapshot to {Conn} for {Symbol}", Context.ConnectionId, symbol);
                }
                else
                {
                    _logger.LogWarning($"No order book data found for {symbol}");
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

        // 新增: 订阅最新成交与中间价
        public async Task SubscribeTicker(string symbol)
        {
            var group = $"ticker_{symbol}";
            await Groups.AddToGroupAsync(Context.ConnectionId, group);
            await Clients.Caller.SendAsync("TickerSubscribed", symbol);
        }

        public async Task UnsubscribeTicker(string symbol)
        {
            var group = $"ticker_{symbol}";
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
            await Clients.Caller.SendAsync("TickerUnsubscribed", symbol);
        }

        // 订阅实时成交数据
        public async Task SubscribeTrades(string symbol)
        {
            var group = $"trades_{symbol}";
            await Groups.AddToGroupAsync(Context.ConnectionId, group);
            _logger.LogInformation("✅ [TradingHub] 客户端 {ConnectionId} 订阅成交数据: {Group}", Context.ConnectionId, group);
            await Clients.Caller.SendAsync("TradesSubscribed", symbol);
        }

        public async Task UnsubscribeTrades(string symbol)
        {
            var group = $"trades_{symbol}";
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
            _logger.LogInformation("❌ [TradingHub] 客户端 {ConnectionId} 取消订阅成交数据: {Group}", Context.ConnectionId, group);
            await Clients.Caller.SendAsync("TradesUnsubscribed", symbol);
        }

        // 订阅用户个人数据(订单、成交、资产)
        public async Task SubscribeUserData(int userId)
        {
            var userGroup = $"user_{userId}";
            await Groups.AddToGroupAsync(Context.ConnectionId, userGroup);
            _logger.LogInformation("✅ [TradingHub] 客户端 {ConnectionId} 订阅用户数据: UserId={UserId}", Context.ConnectionId, userId);
            await Clients.Caller.SendAsync("UserDataSubscribed", userId);
        }

        public async Task UnsubscribeUserData(int userId)
        {
            var userGroup = $"user_{userId}";
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, userGroup);
            _logger.LogInformation("❌ [TradingHub] 客户端 {ConnectionId} 取消订阅用户数据: UserId={UserId}", Context.ConnectionId, userId);
            await Clients.Caller.SendAsync("UserDataUnsubscribed", userId);
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
