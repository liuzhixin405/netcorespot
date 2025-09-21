import { BaseApi } from './base';
import { 
  CryptoPrice, 
  TradingPair, 
  Order, 
  Trade, 
  Asset, 
  KLineData, 
  OrderBook, 
  TradeFormData,
  TimeFrame 
} from '../types';

export class TradingApi extends BaseApi {
  // 获取加密货币价格列表
  async getCryptoPrices(): Promise<CryptoPrice[]> {
    return this.get<CryptoPrice[]>('/crypto/prices');
  }

  // 获取单个加密货币价格
  async getCryptoPrice(symbol: string): Promise<CryptoPrice> {
    return this.get<CryptoPrice>(`/crypto/price/${symbol}`);
  }

  // 获取交易对列表
  async getTradingPairs(): Promise<TradingPair[]> {
    return this.get<TradingPair[]>('/trading/pairs');
  }

  // 获取K线数据
  async getKLineData(symbol: string, timeFrame: string, limit: number = 100): Promise<KLineData[]> {
    return this.get<KLineData[]>(`/trading/klines/${symbol}?interval=${timeFrame}&limit=${limit}`);
  }

  // 获取订单簿数据
  async getOrderBook(symbol: string, limit: number = 20): Promise<OrderBook> {
    return this.get<OrderBook>(`/trading/orderbook/${symbol}`, {
      params: { limit }
    });
  }

  // 获取最近成交记录
  async getRecentTrades(symbol: string, limit: number = 20): Promise<Trade[]> {
    return this.get<Trade[]>(`/trading/trades/${symbol}`, {
      params: { limit }
    });
  }

  // 提交交易订单
  async submitOrder(orderData: TradeFormData): Promise<Order> {
    return this.post<Order>('/trading/orders', orderData);
  }

  // 获取用户当前委托
  async getCurrentOrders(symbol?: string): Promise<Order[]> {
    const params = symbol ? { symbol } : {};
    return this.get<Order[]>('/trading/orders/current', { params });
  }

  // 获取用户历史委托
  async getOrderHistory(symbol?: string, limit: number = 50): Promise<Order[]> {
    const params = symbol ? { symbol, limit } : { limit };
    return this.get<Order[]>('/trading/orders/history', { params });
  }

  // 取消订单
  async cancelOrder(orderId: string): Promise<void> {
    return this.delete<void>(`/trading/orders/${orderId}`);
  }

  // 获取用户成交记录
  async getTradeHistory(symbol?: string, limit: number = 50): Promise<Trade[]> {
    const params = symbol ? { symbol, limit } : { limit };
    return this.get<Trade[]>('/trading/trades/history', { params });
  }

  // 获取用户资产
  async getAssets(): Promise<Asset[]> {
    return this.get<Asset[]>('/trading/assets');
  }

  // 获取单个资产
  async getAsset(symbol: string): Promise<Asset> {
    return this.get<Asset>(`/trading/assets/${symbol}`);
  }

  // 获取时间框架列表
  async getTimeFrames(): Promise<TimeFrame[]> {
    return this.get<TimeFrame[]>('/trading/timeframes');
  }

  // 获取交易统计
  async getTradingStats(): Promise<{
    totalVolume: number;
    totalTrades: number;
    profitLoss: number;
  }> {
    return this.get('/trading/stats');
  }
}

// 导出单例实例
export const tradingApi = new TradingApi();
