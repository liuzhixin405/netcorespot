import { useState, useEffect, useCallback, useRef } from 'react';
import { signalRClient } from '../services/signalRClient';

export interface TickerData {
  symbol: string;
  lastPrice?: number;
  lastQuantity?: number;
  bestBid?: number;
  bestAsk?: number;
  midPrice?: number;
  timestamp: number;
}

interface UseSignalRTickerReturn {
  tickerData: TickerData | null;
  loading: boolean;
  error: string | null;
  isConnected: boolean;
  reconnect: () => void;
}

export const useSignalRTicker = (symbol: string): UseSignalRTickerReturn => {
  const [tickerData, setTickerData] = useState<TickerData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [isConnected, setIsConnected] = useState(false);
  
  const unsubscribeRef = useRef<(() => void) | null>(null);

  // 处理ticker更新
  const handleTickerUpdate = useCallback((data: any) => {
    if ((window as any).__SR_DEBUG) console.log('[Hook][Ticker] update raw=', data);
    if (!data || !data.symbol) return;

    const newTickerData: TickerData = {
      symbol: data.symbol,
      lastPrice: data.lastPrice ? Number(data.lastPrice) : undefined,
      lastQuantity: data.lastQuantity ? Number(data.lastQuantity) : undefined,
      bestBid: data.bestBid ? Number(data.bestBid) : undefined,
      bestAsk: data.bestAsk ? Number(data.bestAsk) : undefined,
      midPrice: data.midPrice ? Number(data.midPrice) : undefined,
      timestamp: data.timestamp || Date.now()
    };

    setTickerData(newTickerData);
    setError(null);
    setIsConnected(true);
    setLoading(false);
  }, []);

  // 处理连接错误
  const handleError = useCallback((err: any) => {
    setError('SignalR Ticker连接失败');
    setIsConnected(false);
    setLoading(false);
  }, []);

  // 启动SignalR ticker订阅
  const startTickerSubscription = useCallback(async () => {
    if ((window as any).__SR_DEBUG) console.log('[Hook][Ticker] start subscription symbol=', symbol);
    if (!symbol) return;

    try {
      if (unsubscribeRef.current) {
        unsubscribeRef.current();
        unsubscribeRef.current = null;
      }

      const unsubscribe = await signalRClient.subscribeTicker(
        symbol,
        handleTickerUpdate,
        handleError
      );
      
      unsubscribeRef.current = unsubscribe;
      setIsConnected(signalRClient.isConnected());
      setLoading(false);
    } catch (err: any) {
      setError(`SignalR Ticker连接失败: ${err.message}`);
      setIsConnected(false);
      setLoading(false);
    }
  }, [symbol, handleTickerUpdate, handleError]);

  // 手动重连
  const reconnect = useCallback(() => {
    startTickerSubscription();
  }, [startTickerSubscription]);

  // 启动订阅
  useEffect(() => {
    if (symbol) {
      startTickerSubscription();
    }
    return () => {
      if (unsubscribeRef.current) {
        unsubscribeRef.current();
        unsubscribeRef.current = null;
      }
    };
  }, [symbol, startTickerSubscription]);

  return {
    tickerData,
    loading,
    error,
    isConnected,
    reconnect
  };
};
