import { useState, useEffect, useCallback, useRef, useMemo } from 'react';
import { KLineData } from '../types';
import { useKLineData } from './useKLineData';
import { useSignalRKLineData } from './useSignalRKLineData';
import { KLineCalculator } from '../services/klineCalculator';

// 合并历史数据和实时数据的辅助函数
const mergeHistoricalAndRealtimeData = (
  historicalData: KLineData[], 
  realtimeData: KLineData[], 
  limit: number
): KLineData[] => {
  // 合并数据并去重
  const allData = [...historicalData, ...realtimeData];
  const uniqueData = allData.reduce((acc, current) => {
    const existingIndex = acc.findIndex(item => item.timestamp === current.timestamp);
    if (existingIndex >= 0) {
      // 如果时间戳相同，使用实时数据（更新现有数据）
      acc[existingIndex] = current;
    } else {
      // 添加新数据
      acc.push(current);
    }
    return acc;
  }, [] as KLineData[]);

  // 按时间排序（从左到右）
  const sortedData = uniqueData.sort((a, b) => a.timestamp - b.timestamp);
  
  // 限制数据量，保留最新的数据
  return sortedData.slice(-limit);
};

interface UseKLineWithRealTimeOptions {
  symbol: string;
  timeframe: string;
  limit?: number;
}

interface UseKLineWithRealTimeReturn {
  data: KLineData[];
  loading: boolean;
  error: string | null;
  isConnected: boolean;
  lastUpdate: number;
  refresh: () => Promise<void>;
  loadMore: (startTime?: number, endTime?: number) => Promise<void>;
  reconnect: () => void;
}

/**
 * K线数据Hook - 结合API获取历史数据和SignalR实时更新
 */
export const useKLineWithRealTime = ({
  symbol,
  timeframe,
  limit = 100
}: UseKLineWithRealTimeOptions): UseKLineWithRealTimeReturn => {
  
  // 使用API获取历史数据
  const {
    klineData: historicalData,
    latestKLine,
    loading: apiLoading,
    error: apiError,
    refresh: apiRefresh,
    loadMore: apiLoadMore,
    updateKLineData: updateHistoricalData
  } = useKLineData({
    symbol,
    interval: timeframe,
    limit,
    autoRefresh: false // 不使用自动刷新，通过SignalR更新
  });

  // 使用SignalR获取实时更新（只订阅1分钟数据）
  const {
    data: realtimeMinuteData,
    loading: signalRLoading,
    error: signalRError,
    isConnected,
    lastUpdate,
    reconnect: signalRReconnect
  } = useSignalRKLineData(symbol, timeframe, limit);

  const [combinedData, setCombinedData] = useState<KLineData[]>([]);
  const [isInitialized, setIsInitialized] = useState(false);

  // 当 symbol 或 timeframe 发生变化时，强制重置初始化状态并重新加载历史数据
  useEffect(() => {
    if (!symbol || !timeframe) return;
    
    // ✅ 完全重置状态
    setIsInitialized(false);
    setCombinedData([]);
    
    // 立即触发历史刷新
    apiRefresh();
  }, [symbol, timeframe, apiRefresh]);

  // 初始化：加载历史数据（仅在 isInitialized=false 且收到新的 historicalData 时执行）
  useEffect(() => {
    if (!isInitialized) {
      if (historicalData.length > 0) {
        // ✅ 有历史数据，使用历史数据初始化
        const sortedHistoricalData = [...historicalData].sort((a, b) => a.timestamp - b.timestamp);
        setCombinedData(sortedHistoricalData.slice(-limit));
        setIsInitialized(true);
      } else if (!apiLoading && (apiError || historicalData.length === 0)) {
        // ✅ 历史数据加载失败或为空，等待1秒后强制初始化（可以使用实时数据）
        const timer = setTimeout(() => {
          setIsInitialized(true);
        }, 1000);
        return () => clearTimeout(timer);
      }
    }
  }, [historicalData, isInitialized, limit, apiLoading, apiError]);

  // 处理实时分钟数据更新 - 即使未初始化也可以显示实时数据
  useEffect(() => {
    // ✅ 添加 symbol 依赖，确保切换时重新评估
    if (realtimeMinuteData.length > 0) {
      setCombinedData(prevData => {
        // ✅ 如果未初始化且没有历史数据，直接使用实时数据
        if (!isInitialized && prevData.length === 0) {
          if (timeframe === '1m') {
            return realtimeMinuteData.slice(-limit);
          } else {
            const calculatedData = KLineCalculator.calculateKLineFromMinutes(realtimeMinuteData, timeframe);
            return calculatedData.slice(-limit);
          }
        }
        
        // 获取最新的实时分钟数据
        const latestMinuteData = realtimeMinuteData[realtimeMinuteData.length - 1];
        if (!latestMinuteData) return prevData;

        if (timeframe === '1m') {
          // 1分钟数据：合并历史数据和实时数据
          return mergeHistoricalAndRealtimeData(prevData, [latestMinuteData], limit);
        } else {
          // 其他时间框架：需要从分钟数据计算
          const calculatedData = KLineCalculator.calculateKLineFromMinutes(
            realtimeMinuteData, 
            timeframe
          );
          
          if (calculatedData.length > 0) {
            const latestCalculatedData = calculatedData[calculatedData.length - 1];
            return mergeHistoricalAndRealtimeData(prevData, [latestCalculatedData], limit);
          }
          
          return prevData;
        }
      });
    }
  }, [realtimeMinuteData, isInitialized, symbol, timeframe, limit]);

  // 实时更新单个K线数据（用于增量更新）
  const handleRealTimeKLineUpdate = useCallback((klineData: KLineData, isNewKLine: boolean) => {

    setCombinedData(prevData => {
      // 使用统一的合并逻辑
      return mergeHistoricalAndRealtimeData(prevData, [klineData], limit);
    });
  }, [symbol, timeframe, limit]);

  // 手动刷新
  const refresh = useCallback(async () => {
    setIsInitialized(false);
    await apiRefresh();
  }, [apiRefresh]);

  // 加载更多历史数据
  const loadMore = useCallback(async (startTime?: number, endTime?: number) => {
    await apiLoadMore(startTime, endTime);
  }, [apiLoadMore]);

  // ✅ 优化加载状态：如果有任何数据就不应该显示加载中
  const loading = useMemo(() => {
    // 如果已经有数据（历史或实时），就不显示加载中
    if (combinedData.length > 0) return false;
    // 如果正在加载且没有数据，显示加载中
    return apiLoading || signalRLoading;
  }, [combinedData.length, apiLoading, signalRLoading]);
  
  // 计算错误状态
  const error = apiError || signalRError;

  return {
    data: combinedData,
    loading,
    error,
    isConnected,
    lastUpdate,
    refresh,
    loadMore,
    reconnect: signalRReconnect
  };
};

export default useKLineWithRealTime;
