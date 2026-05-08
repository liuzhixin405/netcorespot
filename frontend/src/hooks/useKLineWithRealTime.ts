import { useState, useEffect, useCallback, useMemo } from 'react';
import { KLineData } from '../types';
import { useKLineData } from './useKLineData';
import { useSignalRKLineData } from './useSignalRKLineData';
import { KLineCalculator } from '../services/klineCalculator';

const getIntervalMs = (timeframe: string): number => {
  switch (timeframe) {
    case '1m':
      return 60 * 1000;
    case '5m':
      return 5 * 60 * 1000;
    case '15m':
      return 15 * 60 * 1000;
    case '1h':
      return 60 * 60 * 1000;
    case '4h':
      return 4 * 60 * 60 * 1000;
    case '1d':
      return 24 * 60 * 60 * 1000;
    case '1w':
      return 7 * 24 * 60 * 60 * 1000;
    default:
      return 60 * 1000;
  }
};

const trimToLatestContinuousSegment = (data: KLineData[], timeframe: string): KLineData[] => {
  if (data.length <= 2) return data;

  const maxGap = getIntervalMs(timeframe) * 3;
  let startIndex = 0;

  for (let index = data.length - 1; index > 0; index -= 1) {
    const gap = data[index].timestamp - data[index - 1].timestamp;
    if (gap > maxGap) {
      startIndex = index;
      break;
    }
  }

  return data.slice(startIndex);
};

const mergeHistoricalAndRealtimeData = (
  historicalData: KLineData[],
  realtimeData: KLineData[],
  limit: number,
  timeframe: string
): KLineData[] => {
  const allData = [...historicalData, ...realtimeData];
  const uniqueData = allData.reduce((accumulator, current) => {
    const existingIndex = accumulator.findIndex(item => item.timestamp === current.timestamp);
    if (existingIndex >= 0) {
      accumulator[existingIndex] = current;
    } else {
      accumulator.push(current);
    }
    return accumulator;
  }, [] as KLineData[]);

  const sortedData = uniqueData.sort((a, b) => a.timestamp - b.timestamp);
  const continuousData = trimToLatestContinuousSegment(sortedData, timeframe);
  return continuousData.slice(-limit);
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

export const useKLineWithRealTime = ({
  symbol,
  timeframe,
  limit = 100,
}: UseKLineWithRealTimeOptions): UseKLineWithRealTimeReturn => {
  const {
    klineData: historicalData,
    loading: apiLoading,
    error: apiError,
    refresh: apiRefresh,
    loadMore: apiLoadMore,
  } = useKLineData({
    symbol,
    interval: timeframe,
    limit,
    autoRefresh: false,
  });

  const {
    data: realtimeMinuteData,
    loading: signalRLoading,
    error: signalRError,
    isConnected,
    lastUpdate,
    reconnect: signalRReconnect,
  } = useSignalRKLineData(symbol, timeframe, limit);

  const [combinedData, setCombinedData] = useState<KLineData[]>([]);
  const [isInitialized, setIsInitialized] = useState(false);

  useEffect(() => {
    if (!symbol || !timeframe) return;
    setIsInitialized(false);
    setCombinedData([]);
    apiRefresh();
  }, [symbol, timeframe, apiRefresh]);

  useEffect(() => {
    if (isInitialized) return;

    if (historicalData.length > 0) {
      const sortedHistoricalData = [...historicalData].sort((a, b) => a.timestamp - b.timestamp);
      setCombinedData(trimToLatestContinuousSegment(sortedHistoricalData, timeframe).slice(-limit));
      setIsInitialized(true);
      return;
    }

    if (!apiLoading && (apiError || historicalData.length === 0)) {
      const timer = setTimeout(() => setIsInitialized(true), 1000);
      return () => clearTimeout(timer);
    }
  }, [historicalData, isInitialized, limit, apiLoading, apiError, timeframe]);

  useEffect(() => {
    if (realtimeMinuteData.length === 0) return;

    setCombinedData(previousData => {
      if (!isInitialized && previousData.length === 0) {
        if (timeframe === '1m') {
          return trimToLatestContinuousSegment(realtimeMinuteData.slice(-limit), timeframe);
        }

        const calculatedData = KLineCalculator.calculateKLineFromMinutes(realtimeMinuteData, timeframe);
        return trimToLatestContinuousSegment(calculatedData.slice(-limit), timeframe);
      }

      const latestMinuteData = realtimeMinuteData[realtimeMinuteData.length - 1];
      if (!latestMinuteData) return previousData;

      if (timeframe === '1m') {
        return mergeHistoricalAndRealtimeData(previousData, [latestMinuteData], limit, timeframe);
      }

      const calculatedData = KLineCalculator.calculateKLineFromMinutes(realtimeMinuteData, timeframe);
      if (calculatedData.length === 0) return previousData;

      const latestCalculatedData = calculatedData[calculatedData.length - 1];
      return mergeHistoricalAndRealtimeData(previousData, [latestCalculatedData], limit, timeframe);
    });
  }, [realtimeMinuteData, isInitialized, timeframe, limit]);

  const refresh = useCallback(async () => {
    setIsInitialized(false);
    await apiRefresh();
  }, [apiRefresh]);

  const loadMore = useCallback(async (startTime?: number, endTime?: number) => {
    await apiLoadMore(startTime, endTime);
  }, [apiLoadMore]);

  const loading = useMemo(() => {
    if (combinedData.length > 0) return false;
    return apiLoading || signalRLoading;
  }, [combinedData.length, apiLoading, signalRLoading]);

  const error = apiError || signalRError;

  return {
    data: combinedData,
    loading,
    error,
    isConnected,
    lastUpdate,
    refresh,
    loadMore,
    reconnect: signalRReconnect,
  };
};

export default useKLineWithRealTime;
