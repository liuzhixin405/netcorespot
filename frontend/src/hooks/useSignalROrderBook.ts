import { useState, useEffect, useCallback, useRef } from 'react';
import { signalRClient } from '../services/signalRClient';

export interface OrderBookLevel {
  price: number;
  amount: number;
  total: number;
}

export interface OrderBookData {
  symbol: string;
  bids: OrderBookLevel[];
  asks: OrderBookLevel[];
  timestamp: number;
}

interface UseSignalROrderBookReturn {
  orderBookData: OrderBookData | null;
  loading: boolean;
  error: string | null;
  isConnected: boolean;
  lastUpdate: number;
  reconnect: () => void;
}

export const useSignalROrderBook = (
  symbol: string,
  depth: number = 5
): UseSignalROrderBookReturn => {
  const [orderBookData, setOrderBookData] = useState<OrderBookData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [isConnected, setIsConnected] = useState(false);
  const [lastUpdate, setLastUpdate] = useState(0);
  
  const unsubscribeRef = useRef<(() => void) | null>(null);
  const reconnectTimeoutRef = useRef<NodeJS.Timeout | null>(null);
  const reconnectAttemptsRef = useRef(0);
  const maxReconnectAttempts = 5;
  
  // 本地订单簿缓存，用于增量更新
  const localOrderBookRef = useRef<{
    bids: Map<number, OrderBookLevel>;
    asks: Map<number, OrderBookLevel>;
  }>({
    bids: new Map(),
    asks: new Map()
  });

  // 防抖更新机制
  const updateTimeoutRef = useRef<NodeJS.Timeout | null>(null);
  const pendingUpdateRef = useRef<any>(null);

  // 处理订单簿数据更新
  const handleOrderBookData = useCallback((data: any) => {
    // 如果是快照数据（首次加载或重新同步），立即处理
    if (data.type === 'snapshot') {
      const orderBook: OrderBookData = {
        symbol: data.symbol,
        bids: data.bids || [],
        asks: data.asks || [],
        timestamp: data.timestamp
      };
      
      // 更新本地缓存
      localOrderBookRef.current.bids.clear();
      localOrderBookRef.current.asks.clear();
      
      data.bids?.forEach((bid: OrderBookLevel) => {
        localOrderBookRef.current.bids.set(bid.price, bid);
      });
      
      data.asks?.forEach((ask: OrderBookLevel) => {
        localOrderBookRef.current.asks.set(ask.price, ask);
      });
      
      setOrderBookData(orderBook);
    } else {
      // 增量更新使用防抖机制
      pendingUpdateRef.current = data;
      
      if (updateTimeoutRef.current) {
        clearTimeout(updateTimeoutRef.current);
      }
      
      updateTimeoutRef.current = setTimeout(() => {
        if (pendingUpdateRef.current) {
          updateOrderBookIncremental(pendingUpdateRef.current);
          pendingUpdateRef.current = null;
        }
      }, 50); // 减少防抖延迟到50ms
    }
    
    setLastUpdate(Date.now());
    setError(null);
    setIsConnected(true);
    setLoading(false);
  }, []); // 移除 orderBookData 依赖

  // 增量更新订单簿
  const updateOrderBookIncremental = useCallback((data: any) => {
    const { bids, asks } = data;
    
    // 更新买单
    if (bids) {
      bids.forEach((bid: OrderBookLevel) => {
        if (bid.amount === 0) {
          // 数量为0表示删除该价格级别
          localOrderBookRef.current.bids.delete(bid.price);
        } else {
          // 更新或添加价格级别
          localOrderBookRef.current.bids.set(bid.price, bid);
        }
      });
    }
    
    // 更新卖单
    if (asks) {
      asks.forEach((ask: OrderBookLevel) => {
        if (ask.amount === 0) {
          // 数量为0表示删除该价格级别
          localOrderBookRef.current.asks.delete(ask.price);
        } else {
          // 更新或添加价格级别
          localOrderBookRef.current.asks.set(ask.price, ask);
        }
      });
    }
    
    // 转换为数组并排序
    const sortedBids = Array.from(localOrderBookRef.current.bids.values())
      .sort((a, b) => b.price - a.price)
      .slice(0, depth);
    
    const sortedAsks = Array.from(localOrderBookRef.current.asks.values())
      .sort((a, b) => a.price - b.price)
      .slice(0, depth);
    
    // 计算累计数量
    let bidTotal = 0;
    const bidsWithTotal = sortedBids.map(bid => {
      bidTotal += bid.amount;
      return { ...bid, total: bidTotal };
    });
    
    let askTotal = 0;
    const asksWithTotal = sortedAsks.map(ask => {
      askTotal += ask.amount;
      return { ...ask, total: askTotal };
    });
    
    const updatedOrderBook: OrderBookData = {
      symbol: data.symbol,
      bids: bidsWithTotal,
      asks: asksWithTotal,
      timestamp: data.timestamp
    };
    
    setOrderBookData(updatedOrderBook);
  }, [depth]);

  // 处理连接错误
  const handleError = useCallback((err: any) => {
    console.error('OrderBook SignalR error:', err);
    setError('订单簿连接失败');
    setIsConnected(false);
    setLoading(false);
    
    // 自动重连逻辑
    scheduleReconnect();
  }, []);

  // 自动重连调度
  const scheduleReconnect = useCallback(() => {
    if (reconnectAttemptsRef.current >= maxReconnectAttempts) {
      return;
    }

    // 清除之前的重连定时器
    if (reconnectTimeoutRef.current) {
      clearTimeout(reconnectTimeoutRef.current);
    }

    // 计算重连延迟（指数退避）
    const delay = Math.min(1000 * Math.pow(2, reconnectAttemptsRef.current), 10000);
    reconnectAttemptsRef.current++;


    reconnectTimeoutRef.current = setTimeout(() => {
      startOrderBookSubscription();
    }, delay);
  }, []);

  // 重置重连计数器
  const resetReconnectAttempts = useCallback(() => {
    reconnectAttemptsRef.current = 0;
    if (reconnectTimeoutRef.current) {
      clearTimeout(reconnectTimeoutRef.current);
      reconnectTimeoutRef.current = null;
    }
  }, []);

  // 启动SignalR订单簿订阅
  const startOrderBookSubscription = useCallback(async () => {
    if (!symbol) return;
    
    setLoading(true);
    setError(null);
    setIsConnected(false);
    setOrderBookData(null);
    
    try {
      // 取消之前的订阅
      if (unsubscribeRef.current) {
        unsubscribeRef.current();
        unsubscribeRef.current = null;
      }
      
      // 确保SignalR连接已建立，最多等待5秒
      let attempts = 0;
      const maxAttempts = 10;
      let isConnected = false;
      
      while (attempts < maxAttempts && !isConnected) {
        isConnected = await signalRClient.connect();
        if (!isConnected) {
          attempts++;
          await new Promise(resolve => setTimeout(resolve, 500));
        }
      }
      
      if (!isConnected) {
        throw new Error('SignalR连接超时');
      }
      
      // 短暂延迟确保连接稳定
      await new Promise(resolve => setTimeout(resolve, 500));
      
      // 订阅订单簿数据
      const unsubscribe = await signalRClient.subscribeOrderBook(
        symbol,
        handleOrderBookData,
        handleError
      );
      
      unsubscribeRef.current = unsubscribe;

      // 订阅后500ms若未收到数据仍结束loading, 以避免UI长时间加载
      setTimeout(() => {
        if (loading) {
          setLoading(false);
          setIsConnected(signalRClient.isConnected());
        }
      }, 500);
      
      // 连接成功，重置重连计数器
      resetReconnectAttempts();
      
    } catch (err: any) {
      console.error('OrderBook subscription failed:', err);
      setError(`订单簿连接失败: ${err.message}`);
      setIsConnected(false);
      setLoading(false);
      
      // 连接失败，尝试重连
      scheduleReconnect();
    }
  }, [symbol, handleOrderBookData, handleError, resetReconnectAttempts, scheduleReconnect, depth]);

  // 手动重连
  const reconnect = useCallback(() => {
    startOrderBookSubscription();
  }, [startOrderBookSubscription]);

  // 只在symbol变化时重新订阅
  useEffect(() => {
    startOrderBookSubscription();
    
    return () => {
      // 清理订阅
      if (unsubscribeRef.current) {
        unsubscribeRef.current();
        unsubscribeRef.current = null;
      }
      
      // 清理重连定时器
      if (reconnectTimeoutRef.current) {
        clearTimeout(reconnectTimeoutRef.current);
        reconnectTimeoutRef.current = null;
      }
      
      // 清理防抖定时器
      if (updateTimeoutRef.current) {
        clearTimeout(updateTimeoutRef.current);
        updateTimeoutRef.current = null;
      }
      
      // 重置重连计数器
      reconnectAttemptsRef.current = 0;
    };
  }, [symbol, startOrderBookSubscription]);

  return {
    orderBookData,
    loading,
    error,
    isConnected,
    lastUpdate,
    reconnect
  };
};
