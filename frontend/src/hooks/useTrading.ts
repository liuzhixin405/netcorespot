import { useState, useEffect, useCallback } from 'react';
import { tradingService } from '../services/tradingService';
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

// 获取加密货币价格
export const useCryptoPrices = () => {
  const [prices, setPrices] = useState<CryptoPrice[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchPrices = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await tradingService.getCryptoPrices();
      setPrices(data);
    } catch (err) {
      setError('获取价格失败');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchPrices();
  }, [fetchPrices]);

  return { prices, loading, error, refetch: fetchPrices };
};

// 获取交易对信息
export const useTradingPair = (symbol: string) => {
  const [pair, setPair] = useState<TradingPair | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchPair = useCallback(async () => {
    if (!symbol) return;
    
    setLoading(true);
    setError(null);
    try {
      const data = await tradingService.getTradingPair(symbol);
      setPair(data);
    } catch (err) {
      setError('获取交易对信息失败');
    } finally {
      setLoading(false);
    }
  }, [symbol]);

  useEffect(() => {
    fetchPair();
  }, [fetchPair]);

  return { pair, loading, error, refetch: fetchPair };
};

// 获取K线数据
export const useKLineData = (symbol: string, timeFrame: string, limit: number = 100) => {
  const [data, setData] = useState<KLineData[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchKLineData = useCallback(async () => {
    if (!symbol || !timeFrame) return;
    
    setLoading(true);
    setError(null);
    try {
      const klineData = await tradingService.getKLineData(symbol, timeFrame, limit);
      setData(klineData);
    } catch (err) {
      setError('获取K线数据失败');
    } finally {
      setLoading(false);
    }
  }, [symbol, timeFrame, limit]);

  useEffect(() => {
    fetchKLineData();
  }, [fetchKLineData]);

  return { data, loading, error, refetch: fetchKLineData };
};

// 获取订单簿
export const useOrderBook = (symbol: string, limit: number = 20) => {
  const [orderBook, setOrderBook] = useState<OrderBook | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchOrderBook = useCallback(async () => {
    if (!symbol) return;
    
    setLoading(true);
    setError(null);
    try {
      const data = await tradingService.getOrderBook(symbol, limit);
      setOrderBook(data);
    } catch (err) {
      setError('获取订单簿失败');
    } finally {
      setLoading(false);
    }
  }, [symbol, limit]);

  useEffect(() => {
    fetchOrderBook();
  }, [fetchOrderBook]);

  return { orderBook, loading, error, refetch: fetchOrderBook };
};

// 获取最近成交
export const useRecentTrades = (symbol: string, limit: number = 20) => {
  const [trades, setTrades] = useState<Trade[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchTrades = useCallback(async () => {
    if (!symbol) return;
    
    setLoading(true);
    setError(null);
    try {
      const data = await tradingService.getRecentTrades(symbol, limit);
      setTrades(data);
    } catch (err) {
      setError('获取最近成交失败');
    } finally {
      setLoading(false);
    }
  }, [symbol, limit]);

  useEffect(() => {
    fetchTrades();
  }, [fetchTrades]);

  return { trades, loading, error, refetch: fetchTrades };
};

// 获取用户订单
export const useUserOrders = (symbol?: string) => {
  const [orders, setOrders] = useState<Order[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchOrders = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await tradingService.getUserOrders(symbol);
      setOrders(data);
    } catch (err) {
      setError('获取用户订单失败');
    } finally {
      setLoading(false);
    }
  }, [symbol]);

  useEffect(() => {
    fetchOrders();
  }, [fetchOrders]);

  return { orders, loading, error, refetch: fetchOrders };
};

// 获取用户资产
export const useUserAssets = () => {
  const [assets, setAssets] = useState<Asset[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchAssets = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await tradingService.getUserAssets();
      setAssets(data);
    } catch (err) {
      setError('获取用户资产失败');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchAssets();
  }, [fetchAssets]);

  return { assets, loading, error, refetch: fetchAssets };
};

// 获取用户成交记录
export const useUserTrades = (symbol?: string) => {
  const [trades, setTrades] = useState<Trade[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchTrades = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await tradingService.getUserTrades(symbol);
      setTrades(data);
    } catch (err) {
      setError('获取用户成交记录失败');
    } finally {
      setLoading(false);
    }
  }, [symbol]);

  useEffect(() => {
    fetchTrades();
  }, [fetchTrades]);

  return { trades, loading, error, refetch: fetchTrades };
};

// 交易表单提交
export const useTradeForm = () => {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submitOrder = useCallback(async (orderData: TradeFormData) => {
    setLoading(true);
    setError(null);
    
    try {
      // 验证表单
      const validation = tradingService.validateTradeForm(orderData);
      if (!validation.valid) {
        setError(validation.errors.join(', '));
        return { success: false };
      }

      const result = await tradingService.submitOrder(orderData);
      if (!result.success) {
        setError(result.error || '订单提交失败');
        return { success: false };
      }

      return { success: true, order: result.order };
    } catch (err) {
      setError('订单提交失败');
      return { success: false };
    } finally {
      setLoading(false);
    }
  }, []);

  return { submitOrder, loading, error };
};

// 获取时间框架
export const useTimeFrames = () => {
  const [timeFrames] = useState<TimeFrame[]>(() => tradingService.getTimeFrames());
  
  return timeFrames;
};
