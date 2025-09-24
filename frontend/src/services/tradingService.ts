import { tradingApi } from '../api/trading';
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

export class TradingService {
  private static instance: TradingService;
  private cache: Map<string, any> = new Map();
  private cacheTimeout = 5000; // 5秒缓存

  private constructor() {}

  public static getInstance(): TradingService {
    if (!TradingService.instance) {
      TradingService.instance = new TradingService();
    }
    return TradingService.instance;
  }

  // 缓存管理
  private getCachedData<T>(key: string): T | null {
    const cached = this.cache.get(key);
    if (cached && Date.now() - cached.timestamp < this.cacheTimeout) {
      return cached.data;
    }
    return null;
  }

  private setCachedData<T>(key: string, data: T): void {
    this.cache.set(key, {
      data,
      timestamp: Date.now()
    });
  }

  // 获取加密货币价格
  async getCryptoPrices(useCache: boolean = true): Promise<CryptoPrice[]> {
    const cacheKey = 'crypto-prices';
    
    if (useCache) {
      const cached = this.getCachedData<CryptoPrice[]>(cacheKey);
      if (cached) return cached;
    }

    try {
      const prices = await tradingApi.getCryptoPrices();
      this.setCachedData(cacheKey, prices);
      return prices;
    } catch (error) {
      console.error('获取加密货币价格失败:', error);
      // 不再返回模拟数据，直接抛出错误
      throw error;
    }
  }

  // 获取交易对信息
  async getTradingPair(symbol: string): Promise<TradingPair | null> {
    try {
      const pairs = await tradingApi.getTradingPairs();
      return pairs.find(pair => pair.symbol === symbol) || null;
    } catch (error) {
      console.error('获取交易对信息失败:', error);
      throw error;
    }
  }

  // 获取K线数据
  async getKLineData(symbol: string, timeFrame: string, limit: number = 100): Promise<KLineData[]> {
    const cacheKey = `kline-${symbol}-${timeFrame}-${limit}`;
    
    const cached = this.getCachedData<KLineData[]>(cacheKey);
    if (cached) return cached;

    try {
      const data = await tradingApi.getKLineData(symbol, timeFrame, limit);
      this.setCachedData(cacheKey, data);
      return data;
    } catch (error) {
      console.error('获取K线数据失败:', error);
      throw error;
    }
  }

  // 获取订单簿
  async getOrderBook(symbol: string, limit: number = 20): Promise<OrderBook> {
    const cacheKey = `orderbook-${symbol}-${limit}`;
    
    const cached = this.getCachedData<OrderBook>(cacheKey);
    if (cached) return cached;

    try {
      const orderBook = await tradingApi.getOrderBook(symbol, limit);
      this.setCachedData(cacheKey, orderBook);
      return orderBook;
    } catch (error) {
      console.error('获取订单簿失败:', error);
      throw error;
    }
  }

  // 获取最近成交
  async getRecentTrades(symbol: string, limit: number = 20): Promise<Trade[]> {
    const cacheKey = `trades-${symbol}-${limit}`;
    
    const cached = this.getCachedData<Trade[]>(cacheKey);
    if (cached) return cached;

    try {
      const trades = await tradingApi.getRecentTrades(symbol, limit);
      this.setCachedData(cacheKey, trades);
      return trades;
    } catch (error) {
      console.error('获取最近成交失败:', error);
      throw error;
    }
  }

  // 提交交易订单
  async submitOrder(orderData: TradeFormData): Promise<{ success: boolean; order?: Order; error?: string }> {
    try {
      const order = await tradingApi.submitOrder(orderData);
      return { success: true, order };
    } catch (error: any) {
      const errorMessage = error.response?.data?.message || '订单提交失败';
      return { success: false, error: errorMessage };
    }
  }

  // 获取用户订单 (全部)
  async getUserOrders(symbol?: string): Promise<Order[]> {
    try {
      const raw = await tradingApi.getAllOrders(symbol);
      return raw.map((o: any) => this.mapOrder(o));
    } catch (error) {
      console.error('获取用户订单失败:', error);
      throw error;
    }
  }

  // 获取未完成订单
  async getOpenOrders(symbol?: string): Promise<Order[]> {
    try {
      const raw = await tradingApi.getOpenOrders(symbol);
      return raw.map((o: any) => this.mapOrder(o));
    } catch (e) {
      console.error('获取未完成订单失败:', e);
      throw e;
    }
  }

  // 获取历史订单 (已成交/已取消)
  async getOrderHistory(symbol?: string): Promise<Order[]> {
    try {
      const raw = await tradingApi.getOrderHistory(symbol);
      return raw.map((o: any) => this.mapOrder(o));
    } catch (e) {
      console.error('获取历史订单失败:', e);
      throw e;
    }
  }

  // 获取用户资产
  async getUserAssets(): Promise<Asset[]> {
    try {
      return await tradingApi.getAssets();
    } catch (error) {
      console.error('获取用户资产失败:', error);
      throw error;
    }
  }

  // 获取用户成交记录
  async getUserTrades(symbol?: string): Promise<Trade[]> {
    try {
      const raw = await tradingApi.getUserTrades(symbol);
      return raw.map((t: any) => this.mapTrade(t));
    } catch (error) {
      console.error('获取用户成交记录失败:', error);
      throw error;
    }
  }

  async cancelOrder(orderId: number) {
    return tradingApi.cancelOrder(orderId);
  }

  async cancelAllOrders(symbol?: string) {
    return tradingApi.cancelAllOrders(symbol);
  }

  // 验证交易表单
  validateTradeForm(formData: TradeFormData): { valid: boolean; errors: string[] } {
    const errors: string[] = [];
    
    if (!formData.symbol) {
      errors.push('请选择交易对');
    }
    
    if (formData.quantity <= 0) {
      errors.push('数量必须大于0');
    }
    
    if (formData.type === 'limit' && (!formData.price || formData.price <= 0)) {
      errors.push('限价单必须设置价格且大于0');
    }
    
    return { valid: errors.length === 0, errors };
  }

  // 计算交易金额
  calculateTradeAmount(quantity: number, price?: number): number {
    return quantity * (price || 0);
  }

  // 计算手续费
  calculateFee(amount: number, feeRate: number = 0.001): number {
    return amount * feeRate;
  }

  // 获取时间框架列表
  getTimeFrames(): TimeFrame[] {
    return [
      { value: '1m', label: '1分钟' },
      { value: '5m', label: '5分钟' },
      { value: '15m', label: '15分钟' },
      { value: '1h', label: '1小时' },
      { value: '4h', label: '4小时' },
      { value: '1d', label: '1天' },
    ];
  }

  private normalizeStatus(raw: string): Order['status'] {
    const map: Record<string, Order['status']> = {
      Pending: 'pending',
      Active: 'active',
      PartiallyFilled: 'partial',
      Filled: 'filled',
      Cancelled: 'cancelled',
      pending: 'pending',
      active: 'active',
      partial: 'partial',
      filled: 'filled',
      cancelled: 'cancelled'
    };
    return map[raw] || 'pending';
  }

  private mapOrder(o: any): Order {
    return {
      id: o.id,
      orderId: o.orderId,
      symbol: o.symbol,
      side: (o.side || '').toLowerCase() === 'sell' ? 'sell' : 'buy',
      type: (o.type || '').toLowerCase() === 'market' ? 'market' : 'limit',
      quantity: o.quantity,
      price: o.price,
      filledQuantity: o.filledQuantity,
      remainingQuantity: o.remainingQuantity,
      status: this.normalizeStatus(o.status),
      createdAt: o.createdAt,
      updatedAt: o.updatedAt,
      averagePrice: o.averagePrice
    };
  }

  private mapTrade(t: any): Trade {
    return {
      id: t.id,
      tradeId: t.tradeId,
      symbol: t.symbol,
      quantity: t.quantity,
      price: t.price,
      fee: t.fee,
      feeAsset: t.feeAsset,
      totalValue: t.totalValue,
      executedAt: t.executedAt,
      side: (t.side || '').toLowerCase() === 'sell' ? 'sell' : (t.side ? 'buy' : undefined)
    };
  }

  // 所有模拟数据方法已清除
  // 现在完全依赖真实的后端API服务
}

// 导出单例实例
export const tradingService = TradingService.getInstance();
