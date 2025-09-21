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
  depth: number = 20
): UseSignalROrderBookReturn => {
  const [orderBookData, setOrderBookData] = useState<OrderBookData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [isConnected, setIsConnected] = useState(false);
  const [lastUpdate, setLastUpdate] = useState(0);
  
  const unsubscribeRef = useRef<(() => void) | null>(null);

  // 处理订单簿数据更新
  const handleOrderBookData = useCallback((data: any) => {
    console.log('OrderBook data received:', data);
    
    const orderBook: OrderBookData = {
      symbol: data.symbol,
      bids: data.bids || [],
      asks: data.asks || [],
      timestamp: data.timestamp
    };
    
    setOrderBookData(orderBook);
    setLastUpdate(Date.now());
    setError(null);
    setIsConnected(true);
    setLoading(false);
  }, []);

  // 处理连接错误
  const handleError = useCallback((err: any) => {
    console.error('OrderBook SignalR error:', err);
    setError('订单簿连接失败');
    setIsConnected(false);
    setLoading(false);
  }, []);

  // 启动SignalR订单簿订阅
  const startOrderBookSubscription = useCallback(async () => {
    if (!symbol) return;
    
    console.log('Starting OrderBook subscription for:', symbol);
    
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
      
      // 短暂延迟确保清理完成
      await new Promise(resolve => setTimeout(resolve, 200));
      
      // 订阅订单簿数据
      const unsubscribe = await signalRClient.subscribeOrderBook(
        symbol,
        handleOrderBookData,
        handleError
      );
      
      unsubscribeRef.current = unsubscribe;
      
    } catch (err: any) {
      console.error('OrderBook subscription failed:', err);
      setError(`订单簿连接失败: ${err.message}`);
      setIsConnected(false);
      setLoading(false);
    }
  }, [symbol, handleOrderBookData, handleError]);

  // 手动重连
  const reconnect = useCallback(() => {
    startOrderBookSubscription();
  }, [startOrderBookSubscription]);

  // 只在symbol变化时重新订阅
  useEffect(() => {
    startOrderBookSubscription();
    
    return () => {
      if (unsubscribeRef.current) {
        unsubscribeRef.current();
        unsubscribeRef.current = null;
      }
    };
  }, [symbol]);

  return {
    orderBookData,
    loading,
    error,
    isConnected,
    lastUpdate,
    reconnect
  };
};
