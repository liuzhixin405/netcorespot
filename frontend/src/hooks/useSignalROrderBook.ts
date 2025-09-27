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

    // 如果当前还没有本地数据, 强制按 snapshot 处理
    if (!orderBookData) {
      // 回退到 snapshot 逻辑
      const snapshotLike = {
        symbol: data.symbol,
        bids: (bids || []).filter((b: OrderBookLevel) => b.amount > 0),
        asks: (asks || []).filter((a: OrderBookLevel) => a.amount > 0),
        timestamp: data.timestamp
      };
      localOrderBookRef.current.bids.clear();
      localOrderBookRef.current.asks.clear();
      snapshotLike.bids.forEach((b: OrderBookLevel) => localOrderBookRef.current.bids.set(b.price, b));
      snapshotLike.asks.forEach((a: OrderBookLevel) => localOrderBookRef.current.asks.set(a.price, a));
      setOrderBookData(snapshotLike);
      return;
    }

    // 记录是否有任何变化
    let hasChanges = false;

    // 仅更新发生变化的档位 (差异合并)
    if (bids) {
      bids.forEach((bid: OrderBookLevel) => {
        if (bid.amount === 0) {
          if (localOrderBookRef.current.bids.has(bid.price)) {
            localOrderBookRef.current.bids.delete(bid.price);
            hasChanges = true;
          }
        } else {
          const existing = localOrderBookRef.current.bids.get(bid.price);
          if (!existing || existing.amount !== bid.amount) {
            localOrderBookRef.current.bids.set(bid.price, bid);
            hasChanges = true;
          }
        }
      });
    }
    if (asks) {
      asks.forEach((ask: OrderBookLevel) => {
        if (ask.amount === 0) {
          if (localOrderBookRef.current.asks.has(ask.price)) {
            localOrderBookRef.current.asks.delete(ask.price);
            hasChanges = true;
          }
        } else {
          const existing = localOrderBookRef.current.asks.get(ask.price);
          if (!existing || existing.amount !== ask.amount) {
            localOrderBookRef.current.asks.set(ask.price, ask);
            hasChanges = true;
          }
        }
      });
    }

    // 只有在有变化时才更新状态
    if (!hasChanges) return;

    // 转换 & 排序 (保持最少重建)
    const sortedBidEntries = Array.from(localOrderBookRef.current.bids.entries()).sort((a,b) => b[0]-a[0]).slice(0, depth);
    const sortedAskEntries = Array.from(localOrderBookRef.current.asks.entries()).sort((a,b) => a[0]-b[0]).slice(0, depth);

    let bidTotal = 0;
    const bidsWithTotal = sortedBidEntries.map(([price, lvl]) => {
      bidTotal += lvl.amount;
      return { ...lvl, total: bidTotal };
    });
    let askTotal = 0;
    const asksWithTotal = sortedAskEntries.map(([price, lvl]) => {
      askTotal += lvl.amount;
      return { ...lvl, total: askTotal };
    });

    // 直接更新状态，因为我们已经知道有变化
    setOrderBookData({ 
      symbol: data.symbol, 
      bids: bidsWithTotal, 
      asks: asksWithTotal, 
      timestamp: data.timestamp 
    });
  }, [depth, orderBookData]);

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
