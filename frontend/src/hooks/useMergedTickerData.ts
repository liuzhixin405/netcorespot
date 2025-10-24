import { useState, useEffect, useCallback, useRef } from 'react';
import { signalRClient } from '../services/signalRClient';
import { PriceData } from './useSignalRPriceData';
import { TickerData } from './useSignalRTicker';

export interface MergedTickerData {
  symbol: string;
  lastPrice?: number;      // 优先 lastPrice
  midPrice?: number;       // 其次 midPrice
  bestBid?: number;
  bestAsk?: number;
  lastQuantity?: number;
  change24h?: number;      // 小数形式 (0.0123 = +1.23%)
  volume24h?: number;
  high24h?: number;
  low24h?: number;
  timestamp: number;       // 最近一次任一来源更新时间
  // 原始帧供调试
  _rawPrice?: any;
  _rawTicker?: any;
}

interface UseMergedReturn {
  data: MergedTickerData | null;
  loading: boolean;
  error: string | null;
  isConnected: boolean;
  reconnect: () => void;
}

/**
 * 合并 PriceUpdate(含24h统计) + LastTradeAndMid(含即时撮合价/中间价/盘口价) 两种事件
 * 只进行一次实际 SignalR 连接, 内部维护独立监听, 避免多个 hook 相互 off 覆盖。
 */
export function useMergedTickerData(symbol: string): UseMergedReturn {
  const [data, setData] = useState<MergedTickerData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [isConnected, setIsConnected] = useState(false);

  const priceUnsubRef = useRef<(() => void) | null>(null);
  const tickerUnsubRef = useRef<(() => void) | null>(null);
  const lastPriceFrameRef = useRef<any>(null);
  const lastTickerFrameRef = useRef<any>(null);
  const mountedRef = useRef(false);

  const buildMerged = useCallback(() => {
    if (!symbol) return;
    const price = lastPriceFrameRef.current;
    const ticker = lastTickerFrameRef.current;
    
    // ✅ 添加调试日志
    console.log('[useMergedTickerData] buildMerged 执行:', {
      symbol,
      hasPrice: !!price,
      hasTicker: !!ticker,
      priceData: price,
      tickerData: ticker
    });
    
    if (!price && !ticker) {
      console.log('[useMergedTickerData] buildMerged 跳过: price 和 ticker 都为空');
      return;
    }

    const ts = Math.max(
      price?.timestamp || 0,
      ticker?.timestamp || 0,
      Date.now()
    );

    setData(prev => {
      const base: MergedTickerData = {
        symbol,
        lastPrice: ticker?.lastPrice ?? price?.price ?? prev?.lastPrice,
        midPrice: ticker?.midPrice ?? prev?.midPrice,
        bestBid: ticker?.bestBid ?? prev?.bestBid,
        bestAsk: ticker?.bestAsk ?? prev?.bestAsk,
        lastQuantity: ticker?.lastQuantity ?? prev?.lastQuantity,
        // 如果本次没有 price 帧则沿用之前的 24h 统计，避免刷新后被 ticker 先到覆盖为 undefined
        change24h: price?.change24h ?? prev?.change24h,
        volume24h: price?.volume24h ?? prev?.volume24h,
        high24h: price?.high24h ?? prev?.high24h,
        low24h: price?.low24h ?? prev?.low24h,
        timestamp: ts,
        _rawPrice: price,
        _rawTicker: ticker
      };
      if (!prev) return base;
      const changed = ['lastPrice','midPrice','bestBid','bestAsk','change24h','volume24h','high24h','low24h']
        .some(k => (prev as any)[k] !== (base as any)[k]);
      return changed ? base : prev;
    });
    // 在 setState 调用后使用最新（可能异步），这里直接用我们构造的 base 逻辑再取一次 dataRef 不必要，简单延迟写缓存
    try {
      const cacheKey = `merged_ticker_cache_${symbol}`;
      // 读取旧缓存以避免把已存在的 24h 数据覆盖为 undefined
      let old: any = null;
      try { old = JSON.parse(localStorage.getItem(cacheKey) || 'null'); } catch {}
      const latest = (() => {
        // 尝试使用刚刚在 state 中的最新合并值（无法同步获取），退化为使用 prev 逻辑 fallback
        // 这里只聚焦不丢失 24h 字段
        const mergedCandidate: any = {
          symbol,
          lastPrice: ticker?.lastPrice ?? price?.price ?? old?.lastPrice,
          midPrice: ticker?.midPrice ?? old?.midPrice,
          bestBid: ticker?.bestBid ?? old?.bestBid,
          bestAsk: ticker?.bestAsk ?? old?.bestAsk,
          lastQuantity: ticker?.lastQuantity ?? old?.lastQuantity,
          change24h: price?.change24h ?? old?.change24h,
            volume24h: price?.volume24h ?? old?.volume24h,
            high24h: price?.high24h ?? old?.high24h,
            low24h: price?.low24h ?? old?.low24h,
            timestamp: ts
        };
        return mergedCandidate;
      })();
      localStorage.setItem(cacheKey, JSON.stringify(latest));
    } catch {}
    setIsConnected(true);
    setLoading(false);
  }, [symbol]);

  const handlePriceUpdate = useCallback((frame: any) => {
    // ✅ 添加调试日志
    console.log('[useMergedTickerData] PriceUpdate 收到:', {
      frame,
      currentSymbol: symbol,
      frameSymbol: frame?.symbol,
      match: frame?.symbol === symbol,
      has24hData: !!(frame?.change24h !== undefined || frame?.volume24h !== undefined)
    });
    
    if (!frame || frame.symbol !== symbol) {
      console.warn('[useMergedTickerData] PriceUpdate 被过滤:', {
        reason: !frame ? 'frame为空' : `symbol不匹配 (收到:${frame.symbol}, 期望:${symbol})`
      });
      return;
    }
    
    console.log('[useMergedTickerData] PriceUpdate 通过检查，更新 lastPriceFrameRef');
    lastPriceFrameRef.current = frame;
    buildMerged();
  }, [symbol, buildMerged]);

  const handleTickerUpdate = useCallback((frame: any) => {
    if (!frame || frame.symbol !== symbol) return;
    lastTickerFrameRef.current = frame;
    buildMerged();
  }, [symbol, buildMerged]);

  const handleError = useCallback((err: any) => {
    setError(err?.message || 'SignalR合并Ticker订阅失败');
    setIsConnected(false);
    setLoading(false);
  }, []);

  const subscribeAll = useCallback(async () => {
    if (!symbol) return;
    setLoading(true);
    setError(null);

    try {
      console.log('[useMergedTickerData] subscribeAll 开始:', symbol);
      
      // ✅ 先清理旧订阅和旧数据
      if (priceUnsubRef.current) { 
        await priceUnsubRef.current(); 
        priceUnsubRef.current = null; 
      }
      if (tickerUnsubRef.current) { 
        await tickerUnsubRef.current(); 
        tickerUnsubRef.current = null; 
      }
      
      // ✅ 清空旧的 ref 数据，避免使用旧交易对的数据
      lastPriceFrameRef.current = null;
      lastTickerFrameRef.current = null;
      
      // PriceUpdate 订阅（单 symbol 写成数组服用现有 API）
      const pu = await signalRClient.subscribePriceData([symbol], handlePriceUpdate, handleError);
      priceUnsubRef.current = pu;

      // LastTradeAndMid 订阅
      const tu = await signalRClient.subscribeTicker(symbol, handleTickerUpdate, handleError);
      tickerUnsubRef.current = tu;

      console.log('[useMergedTickerData] subscribeAll 完成:', symbol);
      setIsConnected(signalRClient.isConnected());
      setLoading(false);
    } catch (e: any) {
      console.error('[useMergedTickerData] subscribeAll 失败:', e);
      setError(e.message || '合并订阅失败');
      setLoading(false);
      setIsConnected(false);
    }
  }, [symbol, handlePriceUpdate, handleTickerUpdate, handleError]);

  const reconnect = useCallback(() => {
    subscribeAll();
  }, [subscribeAll]);

  useEffect(() => {
    mountedRef.current = true;
    // 先尝试恢复缓存，避免刷新瞬间 24h 为空
    try {
      const cacheKey = `merged_ticker_cache_${symbol}`;
      const raw = localStorage.getItem(cacheKey);
      if (raw) {
        const parsed = JSON.parse(raw);
        setData({ ...parsed });
        setLoading(false);
      }
    } catch {}
    subscribeAll();
    return () => {
      mountedRef.current = false;
      if (priceUnsubRef.current) priceUnsubRef.current();
      if (tickerUnsubRef.current) tickerUnsubRef.current();
      priceUnsubRef.current = null;
      tickerUnsubRef.current = null;
      lastPriceFrameRef.current = null;
      lastTickerFrameRef.current = null;
    };
  }, [symbol, subscribeAll]);

  return { data, loading, error, isConnected, reconnect };
}
