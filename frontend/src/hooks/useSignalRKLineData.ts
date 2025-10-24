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
        // 确保按时间排序（从左到右）
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
    setLoading(false);
  }, [limit, symbol, timeframe]);

  // 处理连接错误
  const handleError = useCallback((err: any) => {
    setError('SignalR连接失败');
    setIsConnected(false);
    setLoading(false);
  }, []);

  // 启动SignalR订阅
  const startSignalRSubscription = useCallback(async () => {
    if (!symbol) return;
    
    // ✅ 清空所有数据
    setMinuteData([]);
    setData([]);
    setLoading(true);
    setError(null);
    setIsConnected(false);
    setLastUpdate(0);
    
    try {
      // ✅ 先取消之前的订阅（如果有）
      if (unsubscribeRef.current) {
        await unsubscribeRef.current();
        unsubscribeRef.current = null;
      }
      
      // ✅ 立即订阅，不需要延迟
      const unsubscribe = await signalRClient.subscribeKLineData(
        symbol,
        '1m', // 固定订阅1分钟数据
        handleKLineUpdate,
        handleError
      );
      
      unsubscribeRef.current = unsubscribe;
      
      // ✅ 立即更新状态，不需要延迟
      setLoading(false);
      setIsConnected(signalRClient.isConnected());
      
    } catch (err: any) {
      console.error('[useSignalRKLineData] 订阅失败:', err);
      setError(`SignalR连接失败: ${err.message}`);
      setIsConnected(false);
      setLoading(false);
    }
  }, [symbol, handleKLineUpdate, handleError, timeframe]);

  // 当 symbol 或 timeframe 变化时清空当前展示数据（minuteData 保留由 symbol 驱动重置）
  useEffect(() => {
    setData([]);
  }, [timeframe]);

  // 从1分钟数据计算目标时间段（加入 timeframe 变化触发 & 1m 直接返回 minuteData）
  useEffect(() => {
    if (minuteData.length === 0) {
      setData([]);
      return;
    }
    
    if (timeframe === '1m') {
      // 直接裁剪 minuteData
      const limited = minuteData.slice(-limit);
      setData(limited);
      return;
    }
    
    const calculatedData = KLineCalculator.calculateKLineFromMinutes(minuteData, timeframe);
    const limitedData = calculatedData.slice(-limit);
    setData(limitedData);
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
  }, [symbol, startSignalRSubscription]);

  return {
    data,
    loading,
    error,
    isConnected,
    lastUpdate,
    reconnect
  };
};
