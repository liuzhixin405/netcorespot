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

// 后端 Trading 控制器实际基础路径: /api/trading
// 由于 axios 基础 baseURL 已含 /api, 这里使用相对路径前缀 /trading
const TRADING_BASE = '/trading';

export class TradingApi extends BaseApi {
  // 获取加密货币价格列表 (若后端尚未实现保持原路径 /crypto/prices)
  async getCryptoPrices(): Promise<CryptoPrice[]> {
    return this.get<CryptoPrice[]>('/crypto/prices');
  }

  // 获取单个加密货币价格
  async getCryptoPrice(symbol: string): Promise<CryptoPrice> {
    return this.get<CryptoPrice>(`/crypto/price/${symbol}`);
  }

  // 获取交易对列表
  async getTradingPairs(): Promise<TradingPair[]> {
    return this.get<TradingPair[]>(`${TRADING_BASE}/pairs`);
  }

  // 获取K线数据 (interval 改为 timeFrame)
  async getKLineData(symbol: string, timeFrame: string, limit: number = 100): Promise<KLineData[]> {
    return this.get<KLineData[]>(`${TRADING_BASE}/klines/${symbol}?timeFrame=${timeFrame}&limit=${limit}`);
  }

  // 获取订单簿数据
  async getOrderBook(symbol: string, limit: number = 20): Promise<OrderBook> {
    return this.get<OrderBook>(`${TRADING_BASE}/orderbook/${symbol}`, {
      params: { limit }
    });
  }

  // 获取最近成交记录 (后端当前无公共市场成交接口; TODO: 若后端提供公共 trades 接口再调整)
  async getRecentTrades(symbol: string, limit: number = 20): Promise<Trade[]> {
    // 暂时指向用户成交或保留原不可用路径; 这里先返回用户成交列表筛选符号
    return this.get<Trade[]>(`${TRADING_BASE}/trades`, { params: { symbol, limit } });
  }

  // 提交交易订单
  async submitOrder(orderData: TradeFormData): Promise<Order> {
    return this.post<Order>(`${TRADING_BASE}/orders`, orderData);
  }

  // 获取用户当前委托(开放/部分成交)
  async getOpenOrders(symbol?: string): Promise<Order[]> {
    const params = symbol ? { symbol } : {};
    return this.get<Order[]>(`${TRADING_BASE}/orders/open`, { params });
  }

  // 获取全部订单（可选过滤symbol）
  async getAllOrders(symbol?: string): Promise<Order[]> {
    const params = symbol ? { symbol } : {};
    return this.get<Order[]>(`${TRADING_BASE}/orders`, { params });
  }

  // 获取指定订单
  async getOrder(id: number): Promise<Order> {
    return this.get<Order>(`${TRADING_BASE}/orders/${id}`);
  }

  // 获取订单成交明细
  async getOrderTrades(id: number): Promise<Trade[]> {
    return this.get<Trade[]>(`${TRADING_BASE}/orders/${id}/trades`);
  }

  // 取消订单
  async cancelOrder(orderId: number): Promise<any> {
    return this.delete(`${TRADING_BASE}/orders/${orderId}`);
  }

  // 批量取消
  async cancelAllOrders(symbol?: string): Promise<any> {
    const params = symbol ? { symbol } : {};
    return this.delete(`${TRADING_BASE}/orders`, { params });
  }

  // 用户成交记录
  async getUserTrades(symbol?: string): Promise<Trade[]> {
    const params = symbol ? { symbol } : {};
    return this.get<Trade[]>(`${TRADING_BASE}/trades`, { params });
  }

  // 获取历史订单（已成交或已取消） - 后端尚无对应 /orders/history 接口，保留占位便于未来实现
  async getOrderHistory(_symbol?: string): Promise<Order[]> {
    // TODO: 后端提供历史订单接口后更新，此处暂时复用全部订单并在前端过滤
    return this.getAllOrders(_symbol);
  }

  // 获取用户资产
  async getAssets(): Promise<Asset[]> {
    return this.get<Asset[]>(`${TRADING_BASE}/assets`);
  }

  // 获取单个资产
  async getAsset(symbol: string): Promise<Asset> {
    return this.get<Asset>(`${TRADING_BASE}/assets/${symbol}`);
  }

  // 获取时间框架列表 - 后端无 /timeframes 路由, 由前端本地常量提供; 保留方法兼容旧调用
  async getTimeFrames(): Promise<TimeFrame[]> {
    return [
      { value: '1m', label: '1分钟' },
      { value: '5m', label: '5分钟' },
      { value: '15m', label: '15分钟' },
      { value: '1h', label: '1小时' },
      { value: '4h', label: '4小时' },
      { value: '1d', label: '1天' },
    ];
  }

  // 获取交易统计 (若后端存在 /trades/statistics 则更新为该路径)
  async getTradingStats(): Promise<{
    totalVolume: number;
    totalTrades: number;
    profitLoss: number;
  }> {
    // TODO: 如果后端提供 /trades/statistics 则改为 `${TRADING_BASE}/trades/statistics`
    return this.get(`${TRADING_BASE}/trades/statistics`);
  }
}

// 导出单例实例
export const tradingApi = new TradingApi();
