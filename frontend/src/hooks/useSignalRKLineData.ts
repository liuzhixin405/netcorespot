import { useState, useEffect, useCallback, useRef } from 'react';
import { KLineData } from '../types';
import { signalRClient } from '../services/signalRClient';
import { KLineCalculator } from '../services/klineCalculator';

interface UseSignalRKLineDataReturn {
  data: KLineData[];
  loading: boolean;
  error: string | null;
  isConnected: boolean;
  lastUpdate: number;
  reconnect: () => void;
}

export const useSignalRKLineData = (
  symbol: string,
  timeframe: string,
  limit: number = 100
): UseSignalRKLineDataReturn => {
  const [minuteData, setMinuteData] = useState<KLineData[]>([]);
  const [data, setData] = useState<KLineData[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [isConnected, setIsConnected] = useState(false);
  const [lastUpdate, setLastUpdate] = useState(0);
  
  const unsubscribeRef = useRef<(() => void) | null>(null);

  // 处理历史K线数据
  const handleHistoricalData = useCallback((historicalData: KLineData[]) => {
    setMinuteData(historicalData);
    setLoading(false);
    setError(null);
    setIsConnected(true);
    setLastUpdate(Date.now());
  }, []);

  // 处理实时K线更新
  const handleKLineUpdate = useCallback((klineData: KLineData, isNewKLine: boolean) => {
    
    setMinuteData(prevData => {
      const existingIndex = prevData.findIndex(
        item => item.timestamp === klineData.timestamp
      );
      
      let updatedData;
      if (existingIndex >= 0) {
        // 更新现有K线
        updatedData = [...prevData];
        updatedData[existingIndex] = klineData;
      } else {
        // 添加新K线
        updatedData = [...prevData, klineData];
        updatedData.sort((a, b) => a.timestamp - b.timestamp);
        
        // 保持数据量限制
        const maxMinuteData = limit * 60;
        if (updatedData.length > maxMinuteData) {
          updatedData = updatedData.slice(-maxMinuteData);
        }
      }
      
      return updatedData;
    });
    
    setLastUpdate(Date.now());
    setError(null);
    setIsConnected(true);
  }, [limit]);

  // 处理连接错误
  const handleError = useCallback((err: any) => {
    setError('SignalR连接失败');
    setIsConnected(false);
    setLoading(false);
  }, []);

  // 启动SignalR订阅
  const startSignalRSubscription = useCallback(async () => {
    if (!symbol) return;
    
    // 启动SignalR K线订阅
    
    setMinuteData([]);
    setData([]);
    setLoading(true);
    setError(null);
    setIsConnected(false);
    
    try {
      // 取消之前的订阅
      if (unsubscribeRef.current) {
        unsubscribeRef.current();
        unsubscribeRef.current = null;
      }
      
      // 短暂延迟确保清理完成
      await new Promise(resolve => setTimeout(resolve, 300));
      
      // 始终订阅1分钟K线数据
      const unsubscribe = await signalRClient.subscribeKLineData(
        symbol,
        '1m', // 固定订阅1分钟数据
        handleHistoricalData,
        handleKLineUpdate,
        handleError
      );
      
      unsubscribeRef.current = unsubscribe;
      
    } catch (err: any) {
      setError(`SignalR连接失败: ${err.message}`);
      setIsConnected(false);
      setLoading(false);
    }
  }, [symbol, handleHistoricalData, handleKLineUpdate, handleError]);

  // 从1分钟数据计算目标时间段
  useEffect(() => {
    if (minuteData.length > 0) {
      const calculatedData = KLineCalculator.calculateKLineFromMinutes(minuteData, timeframe);
      const limitedData = calculatedData.slice(-limit);
      setData(limitedData);
      
      // K线数据计算完成
    }
  }, [minuteData, timeframe, limit]);

  // 手动重连
  const reconnect = useCallback(() => {
    startSignalRSubscription();
  }, [startSignalRSubscription]);

  // 只在symbol变化时重新订阅
  useEffect(() => {
    startSignalRSubscription();
    
    return () => {
      if (unsubscribeRef.current) {
        unsubscribeRef.current();
        unsubscribeRef.current = null;
      }
    };
  }, [symbol]); // 只依赖symbol，不依赖timeframe

  return {
    data,
    loading,
    error,
    isConnected,
    lastUpdate,
    reconnect
  };
};
