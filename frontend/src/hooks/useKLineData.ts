import { useState, useEffect, useCallback, useMemo } from 'react';
import { KLineApiService, KLineData } from '../api/kline';

interface UseKLineDataOptions {
  symbol: string;
  interval: string;
  limit?: number;
  autoRefresh?: boolean;
  refreshInterval?: number;
}

interface UseKLineDataReturn {
  klineData: KLineData[];
  latestKLine: KLineData | null;
  loading: boolean;
  error: string | null;
  refresh: () => Promise<void>;
  loadMore: (startTime?: number, endTime?: number) => Promise<void>;
  updateKLineData: (newKLine: KLineData, isNewKLine?: boolean) => void;
}

/**
 * K线数据管理Hook
 * 负责通过API获取历史数据，SignalR负责实时更新
 */
export const useKLineData = ({
  symbol,
  interval,
  limit = 1000,
  autoRefresh = false,
  refreshInterval = 30000
}: UseKLineDataOptions): UseKLineDataReturn => {
  const [klineData, setKlineData] = useState<KLineData[]>([]);
  const [latestKLine, setLatestKLine] = useState<KLineData | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // 获取历史K线数据
  const loadKLineHistory = useCallback(async (startTime?: number, endTime?: number) => {
    if (!symbol || !interval) return;

    setLoading(true);
    setError(null);

    try {
      const response = await KLineApiService.getKLineHistory(
        symbol,
        interval,
        startTime,
        endTime,
        limit
      );

      if (response.success) {
        setKlineData(response.data);
      } else {
        setError('获取K线数据失败');
      }
    } catch (err: any) {
      setError(err.message || '获取K线数据失败');
      console.error('获取K线历史数据失败:', err);
    } finally {
      setLoading(false);
    }
  }, [symbol, interval, limit]);

  // 获取最新K线数据
  const loadLatestKLine = useCallback(async () => {
    if (!symbol || !interval) return;

    try {
      const response = await KLineApiService.getLatestKLine(symbol, interval);
      
      if (response.success) {
        setLatestKLine(response.data);
      }
    } catch (err: any) {
      console.error('获取最新K线数据失败:', err);
    }
  }, [symbol, interval]);

  // 刷新数据
  const refresh = useCallback(async () => {
    try {
      await loadKLineHistory();
    } catch (err) {
      console.error('[useKLineData] 历史数据加载失败:', err);
    }
  }, [loadKLineHistory]);

  // 加载更多数据
  const loadMore = useCallback(async (startTime?: number, endTime?: number) => {
    if (!symbol || !interval) return;

    setLoading(true);
    setError(null);

    try {
      const response = await KLineApiService.getKLineHistory(
        symbol,
        interval,
        startTime,
        endTime,
        limit
      );

      if (response.success) {
        // 合并数据，去重
        setKlineData(prev => {
          const existing = new Set(prev.map(k => k.timestamp));
          const newData = response.data.filter(k => !existing.has(k.timestamp));
          return [...newData, ...prev].sort((a, b) => a.timestamp - b.timestamp);
        });
      }
    } catch (err: any) {
      setError(err.message || '加载更多K线数据失败');
      console.error('加载更多K线数据失败:', err);
    } finally {
      setLoading(false);
    }
  }, [symbol, interval, limit]);

  // 更新K线数据（用于SignalR实时更新）
  const updateKLineData = useCallback((newKLine: KLineData, isNewKLine: boolean = false) => {
    setKlineData(prev => {
      if (isNewKLine) {
        // 新的K线，添加到数组开头
        return [newKLine, ...prev.slice(0, -1)]; // 保持数组长度不变
      } else {
        // 更新当前K线
        const index = prev.findIndex(k => k.timestamp === newKLine.timestamp);
        if (index >= 0) {
          const updated = [...prev];
          updated[index] = newKLine;
          return updated;
        } else {
          // 如果没找到，添加到开头
          return [newKLine, ...prev.slice(0, -1)];
        }
      }
    });

    // 更新最新K线
    setLatestKLine(newKLine);
  }, []);

  // ✅ 切换 symbol 或 interval 时立即清空旧数据
  useEffect(() => {
    setKlineData([]);
    setLatestKLine(null);
    setError(null);
  }, [symbol, interval]);

  // 初始加载（不依赖 refresh，避免循环依赖）
  useEffect(() => {
    if (symbol && interval) {
      loadKLineHistory();
    }
  }, [symbol, interval, loadKLineHistory]);

  // 自动刷新
  useEffect(() => {
    if (!autoRefresh || !symbol || !interval) return;

    const intervalId = setInterval(() => {
      loadLatestKLine();
    }, refreshInterval);

    return () => clearInterval(intervalId);
  }, [autoRefresh, symbol, interval, refreshInterval, loadLatestKLine]);

  // 暴露更新方法给外部使用
  const memoizedUpdateKLineData = useMemo(() => updateKLineData, [updateKLineData]);

  return {
    klineData,
    latestKLine,
    loading,
    error,
    refresh,
    loadMore,
    // 暴露给SignalR使用
    updateKLineData: memoizedUpdateKLineData
  };
};

export default useKLineData;
