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
  const subscribedRef = useRef<Set<string>>(new Set());
  
  // 稳定化symbols数组
  const stableSymbols = useMemo(() => symbols, [symbols.join(',')]);
  const [symbolsKey, setSymbolsKey] = useState('');

  // 处理价格更新
  const handlePriceUpdate = useCallback((priceUpdate: any) => {
    if ((window as any).__SR_DEBUG) console.log('[Hook][Price] update raw=', priceUpdate);
    if(!priceUpdate || !priceUpdate.symbol) return;
    // change24h 后端为小数 (0.0123 = +1.23%)
    const rawChange = Number(priceUpdate.change24h);
    const newPriceData: PriceData = {
      symbol: priceUpdate.symbol,
      price: Number(priceUpdate.price),
      change24h: isNaN(rawChange) ? 0 : rawChange,
      volume24h: Number(priceUpdate.volume24h || 0),
      high24h: Number(priceUpdate.high24h || 0),
      low24h: Number(priceUpdate.low24h || 0),
      timestamp: priceUpdate.timestamp || Date.now()
    };
    // Debug
    // console.debug('PriceUpdate', newPriceData);

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
    if ((window as any).__SR_DEBUG) console.log('[Hook][Price] start subscription symbols=', stableSymbols);
    if (!stableSymbols || stableSymbols.length === 0) return;

    // 计算需要新增和需要移除的符号
    const desired = new Set(stableSymbols);
    const toAdd: string[] = [];
    const toRemove: string[] = [];
    subscribedRef.current.forEach(s => { if (!desired.has(s)) toRemove.push(s); });
    desired.forEach(s => { if (!subscribedRef.current.has(s)) toAdd.push(s); });

    // 如果没有变化且已有数据, 不重复订阅
    if (toAdd.length === 0 && toRemove.length === 0 && Object.keys(priceData).length > 0) {
      setLoading(false);
      setIsConnected(signalRClient.isConnected());
      return;
    }

    try {
      // 取消不需要的
      if (toRemove.length && unsubscribeRef.current) {
        try { await signalRClient.subscribePriceData([], ()=>{}); } catch {}
        toRemove.forEach(r => subscribedRef.current.delete(r));
      }

      // 新增需要的订阅
      if (toAdd.length) {
        const unsubscribe = await signalRClient.subscribePriceData(
          toAdd,
          handlePriceUpdate,
          handleError
        );
        // 这里不直接覆盖 unsubscribeRef (允许多次叠加)。简化起见, 仍然保留最后一次
        unsubscribeRef.current = unsubscribe;
        toAdd.forEach(a => subscribedRef.current.add(a));
      }

      setLoading(false);
      setIsConnected(signalRClient.isConnected());
    } catch (err: any) {
      setError(`SignalR价格连接失败: ${err.message}`);
      setIsConnected(false);
      setLoading(false);
    }
  }, [stableSymbols, handlePriceUpdate, handleError, priceData]);

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

  // 初始订阅后，如果一定时间内没有任何价格数据，填充占位或触发一次后端轮询（TODO: 可替换为 REST 调用）
  useEffect(() => {
    if (!symbolsKey) return;
    const timer = setTimeout(() => {
      // 若仍无数据则保持 isConnected 状态但提示
      if (Object.keys(priceData).length === 0) {
        setIsConnected(signalRClient.isConnected());
        setLoading(false);
      }
    }, 1500);
    return () => clearTimeout(timer);
  }, [symbolsKey, priceData]);

  return {
    priceData,
    loading,
    error,
    isConnected,
    lastUpdate,
    reconnect
  };
};
