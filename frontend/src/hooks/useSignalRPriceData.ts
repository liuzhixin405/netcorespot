import { useState, useEffect, useCallback, useRef, useMemo } from 'react';
import { signalRClient } from '../services/signalRClient';

export interface PriceData {
  symbol: string;
  price: number;
  change24h: number;
  volume24h: number;
  high24h: number;
  low24h: number;
  timestamp: number;
}

interface UseSignalRPriceDataReturn {
  priceData: { [symbol: string]: PriceData };
  loading: boolean;
  error: string | null;
  isConnected: boolean;
  lastUpdate: number;
  reconnect: () => void;
}

export const useSignalRPriceData = (
  symbols: string[]
): UseSignalRPriceDataReturn => {
  const [priceData, setPriceData] = useState<{ [symbol: string]: PriceData }>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [isConnected, setIsConnected] = useState(false);
  const [lastUpdate, setLastUpdate] = useState(0);
  
  const unsubscribeRef = useRef<(() => void) | null>(null);
  
  // 稳定化symbols数组
  const stableSymbols = useMemo(() => symbols, [symbols.join(',')]);
  const [symbolsKey, setSymbolsKey] = useState('');

  // 处理价格更新
  const handlePriceUpdate = useCallback((priceUpdate: any) => {
    
    const newPriceData: PriceData = {
      symbol: priceUpdate.symbol,
      price: Number(priceUpdate.price),
      change24h: Number(priceUpdate.change24h),
      volume24h: Number(priceUpdate.volume24h),
      high24h: Number(priceUpdate.high24h || 0),
      low24h: Number(priceUpdate.low24h || 0),
      timestamp: priceUpdate.timestamp
    };

    setPriceData(prevData => ({
      ...prevData,
      [newPriceData.symbol]: newPriceData
    }));
    
    setLastUpdate(Date.now());
    setError(null);
    setIsConnected(true);
  }, []);

  // 处理连接错误
  const handleError = useCallback((err: any) => {
    // SignalR价格连接错误
    setError('SignalR价格连接失败');
    setIsConnected(false);
    setLoading(false);
  }, []);

  // 启动SignalR价格订阅
  const startPriceSubscription = useCallback(async () => {
    if (!stableSymbols || stableSymbols.length === 0) return;
    
    const newSymbolsKey = stableSymbols.join(',');
    if (newSymbolsKey === symbolsKey) return;

    // 启动SignalR价格订阅
    
    setSymbolsKey(newSymbolsKey);
    setLoading(true);
    setError(null);
    setIsConnected(false);
    setPriceData({});
    
    try {
      // 取消之前的订阅
      if (unsubscribeRef.current) {
        unsubscribeRef.current();
        unsubscribeRef.current = null;
      }
      
      await new Promise(resolve => setTimeout(resolve, 200));
      
      // 创建新的价格数据订阅
      const unsubscribe = await signalRClient.subscribePriceData(
        stableSymbols,
        handlePriceUpdate,
        handleError
      );
      
      unsubscribeRef.current = unsubscribe;

      // 如果订阅后短时间内没有任何push, 也标记为已连接以便UI显示
      setTimeout(() => {
        setLoading(false);
        setIsConnected(signalRClient.isConnected());
      }, 500);
      
    } catch (err: any) {
      // SignalR价格订阅失败
      setError(`SignalR价格连接失败: ${err.message}`);
      setIsConnected(false);
      setLoading(false);
    }
  }, [stableSymbols, symbolsKey, handlePriceUpdate, handleError]);

  // 手动重连
  const reconnect = useCallback(() => {
    startPriceSubscription();
  }, [startPriceSubscription]);

  // 检测symbols变化并启动订阅
  useEffect(() => {
    const newSymbolsKey = stableSymbols.join(',');
    if (newSymbolsKey && newSymbolsKey !== symbolsKey) {
      startPriceSubscription();
    }
  }, [stableSymbols, symbolsKey, startPriceSubscription]);

  // 组件卸载时清理
  useEffect(() => {
    return () => {
      if (unsubscribeRef.current) {
        unsubscribeRef.current();
        unsubscribeRef.current = null;
      }
    };
  }, []);

  return {
    priceData,
    loading,
    error,
    isConnected,
    lastUpdate,
    reconnect
  };
};
