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
  sequence?: number;
}

const buildVisibleLevels = (
  levels: Map<number, OrderBookLevel>,
  depth: number,
  descending: boolean
): OrderBookLevel[] => {
  let total = 0;
  return Array.from(levels.entries())
    .sort(([left], [right]) => descending ? right - left : left - right)
    .slice(0, depth)
    .map(([, level]) => {
      total += level.amount;
      return { ...level, total };
    });
};

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
  depth: number = 10,
  paused = false
): UseSignalROrderBookReturn => {
  const [orderBookData, setOrderBookData] = useState<OrderBookData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [isConnected, setIsConnected] = useState(false);
  const [lastUpdate, setLastUpdate] = useState(0);
  
  const unsubscribeRef = useRef<(() => void) | null>(null);
  const reconnectTimeoutRef = useRef<NodeJS.Timeout | null>(null);
  const reconnectAttemptsRef = useRef(0);
  const receivedSnapshotRef = useRef(false);
  const currentSequenceRef = useRef<number | null>(null);
  const snapshotRequestPendingRef = useRef(false);
  const pendingDeltasRef = useRef<{
    bids: Map<number, { level: OrderBookLevel; sequence: number }>;
    asks: Map<number, { level: OrderBookLevel; sequence: number }>;
  }>({ bids: new Map(), asks: new Map() });
  const maxReconnectAttempts = 5;
  
  // 本地订单簿缓存，用于增量更新
  const localOrderBookRef = useRef<{
    bids: Map<number, OrderBookLevel>;
    asks: Map<number, OrderBookLevel>;
  }>({
    bids: new Map(),
    asks: new Map()
  });

  const pendingCommitRef = useRef<OrderBookData | null>(null);
  const commitTimerRef = useRef<NodeJS.Timeout | null>(null);
  const lastCommitAtRef = useRef(0);
  const commitIntervalMs = 100;
  const pausedRef = useRef(paused);

  const commitOrderBookData = useCallback((next: OrderBookData) => {
    pendingCommitRef.current = next;

    if (pausedRef.current) {
      return;
    }

    const elapsed = Date.now() - lastCommitAtRef.current;
    const delay = Math.max(0, commitIntervalMs - elapsed);

    const commit = () => {
      const pending = pendingCommitRef.current;
      if (!pending) return;

      pendingCommitRef.current = null;
      lastCommitAtRef.current = Date.now();
      setOrderBookData(pending);
      setLastUpdate(Date.now());
      setError(null);
      setIsConnected(true);
      setLoading(false);
    };

    if (delay === 0) {
      commit();
      return;
    }

    if (!commitTimerRef.current) {
      commitTimerRef.current = setTimeout(() => {
        commitTimerRef.current = null;
        commit();
      }, delay);
    }
  }, []);

  useEffect(() => {
    pausedRef.current = paused;
    if (!paused && pendingCommitRef.current) {
      lastCommitAtRef.current = 0;
      commitOrderBookData(pendingCommitRef.current);
    }
  }, [paused, commitOrderBookData]);

  useEffect(() => () => {
    if (commitTimerRef.current) {
      clearTimeout(commitTimerRef.current);
      commitTimerRef.current = null;
    }
  }, []);

  // 增量更新订单簿
  const updateOrderBookIncremental = useCallback((data: any) => {
    const { bids, asks } = data;

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

    // 使用requestAnimationFrame优化渲染性能
    requestAnimationFrame(() => {
      // 直接更新状态，因为我们已经知道有变化
      commitOrderBookData({
        symbol: data.symbol,
        bids: buildVisibleLevels(localOrderBookRef.current.bids, depth, true),
        asks: buildVisibleLevels(localOrderBookRef.current.asks, depth, false),
        timestamp: data.timestamp 
      });
    });
  }, [commitOrderBookData, depth]);

  // 处理订单簿数据更新
  const handleOrderBookData = useCallback((data: any) => {
    const sequence = Number(data.sequence);
    if (data.type === 'snapshot') {
      if (
        Number.isFinite(sequence) &&
        currentSequenceRef.current !== null &&
        sequence < currentSequenceRef.current
      ) {
        return;
      }

      receivedSnapshotRef.current = true;
      snapshotRequestPendingRef.current = false;
      localOrderBookRef.current.bids.clear();
      localOrderBookRef.current.asks.clear();

      (data.bids || []).forEach((bid: OrderBookLevel) => {
        if (bid.amount > 0) localOrderBookRef.current.bids.set(bid.price, bid);
      });
      (data.asks || []).forEach((ask: OrderBookLevel) => {
        if (ask.amount > 0) localOrderBookRef.current.asks.set(ask.price, ask);
      });

      currentSequenceRef.current = Number.isFinite(sequence) ? sequence : null;
      if (currentSequenceRef.current !== null) {
        let latestSequence = currentSequenceRef.current;
        for (const [price, pending] of pendingDeltasRef.current.bids) {
          if (pending.sequence > currentSequenceRef.current) {
            latestSequence = Math.max(latestSequence, pending.sequence);
            if (pending.level.amount > 0) {
              localOrderBookRef.current.bids.set(price, pending.level);
            } else {
              localOrderBookRef.current.bids.delete(price);
            }
          }
        }
        for (const [price, pending] of pendingDeltasRef.current.asks) {
          if (pending.sequence > currentSequenceRef.current) {
            latestSequence = Math.max(latestSequence, pending.sequence);
            if (pending.level.amount > 0) {
              localOrderBookRef.current.asks.set(price, pending.level);
            } else {
              localOrderBookRef.current.asks.delete(price);
            }
          }
        }
        currentSequenceRef.current = latestSequence;
      }
      pendingDeltasRef.current.bids.clear();
      pendingDeltasRef.current.asks.clear();

      commitOrderBookData({
        symbol: data.symbol,
        bids: buildVisibleLevels(localOrderBookRef.current.bids, depth, true),
        asks: buildVisibleLevels(localOrderBookRef.current.asks, depth, false),
        timestamp: data.timestamp
      });
      return;
    }

    if (!Number.isFinite(sequence)) return;
    if (currentSequenceRef.current === null) {
      (data.bids || []).forEach((level: OrderBookLevel) => {
        const existing = pendingDeltasRef.current.bids.get(level.price);
        if (!existing || sequence >= existing.sequence) {
          pendingDeltasRef.current.bids.set(level.price, { level, sequence });
        }
      });
      (data.asks || []).forEach((level: OrderBookLevel) => {
        const existing = pendingDeltasRef.current.asks.get(level.price);
        if (!existing || sequence >= existing.sequence) {
          pendingDeltasRef.current.asks.set(level.price, { level, sequence });
        }
      });
      return;
    }
    if (sequence <= currentSequenceRef.current) return;
    if (sequence > currentSequenceRef.current + 1) {
      if (!snapshotRequestPendingRef.current) {
        snapshotRequestPendingRef.current = true;
        signalRClient.requestOrderBookSnapshot(symbol).catch((error) => {
          snapshotRequestPendingRef.current = false;
          console.error('Order book snapshot resync failed:', error);
        });
      }
      return;
    }

    updateOrderBookIncremental(data);
    currentSequenceRef.current = sequence;
  }, [commitOrderBookData, depth, symbol, updateOrderBookIncremental]);

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
    pendingCommitRef.current = null;
    currentSequenceRef.current = null;
    snapshotRequestPendingRef.current = false;
    pendingDeltasRef.current.bids.clear();
    pendingDeltasRef.current.asks.clear();
    localOrderBookRef.current.bids.clear();
    localOrderBookRef.current.asks.clear();
    if (commitTimerRef.current) {
      clearTimeout(commitTimerRef.current);
      commitTimerRef.current = null;
    }
    receivedSnapshotRef.current = false;
    
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
        if (!receivedSnapshotRef.current) {
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
  }, [symbol, handleOrderBookData, handleError, resetReconnectAttempts, scheduleReconnect]);

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

      pendingCommitRef.current = null;
      if (commitTimerRef.current) {
        clearTimeout(commitTimerRef.current);
        commitTimerRef.current = null;
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
