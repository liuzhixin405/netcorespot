import { useState, useEffect, useCallback, useRef } from 'react';
import { KLineData } from '../types';
import { useKLineData } from './useKLineData';
import { useSignalRKLineData } from './useSignalRKLineData';

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

  // 使用SignalR获取实时更新
  const {
    data: realtimeData,
    loading: signalRLoading,
    error: signalRError,
    isConnected,
    lastUpdate,
    reconnect: signalRReconnect
  } = useSignalRKLineData(symbol, timeframe, limit);

  const [combinedData, setCombinedData] = useState<KLineData[]>([]);
  const [isInitialized, setIsInitialized] = useState(false);

  // 合并历史数据和实时数据
  useEffect(() => {
    if (!isInitialized && historicalData.length > 0) {
      // 首次加载历史数据
      setCombinedData(historicalData);
      setIsInitialized(true);
    }
  }, [historicalData, isInitialized]);

  // 使用实时数据更新（当SignalR有数据时）
  useEffect(() => {
    if (isInitialized && realtimeData.length > 0) {
      setCombinedData(realtimeData);
    }
  }, [realtimeData, isInitialized]);

  // 手动刷新
  const refresh = useCallback(async () => {
    setIsInitialized(false);
    await apiRefresh();
  }, [apiRefresh]);

  // 加载更多历史数据
  const loadMore = useCallback(async (startTime?: number, endTime?: number) => {
    await apiLoadMore(startTime, endTime);
  }, [apiLoadMore]);

  // 计算加载状态
  const loading = apiLoading || signalRLoading;
  
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
