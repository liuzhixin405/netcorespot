import React, { useEffect, useMemo, useRef } from 'react';
import styled from 'styled-components';
import { BarChart3, Maximize2, Wifi, WifiOff } from 'lucide-react';
import {
  createChart,
  ColorType,
  CrosshairMode,
  CandlestickSeries,
  HistogramSeries,
  LineSeries,
  type IChartApi,
  type ISeriesApi,
  type CandlestickData,
  type HistogramData,
  type LineData,
  type Time,
  type SeriesType,
} from 'lightweight-charts';
import { useKLineWithRealTime } from '../../hooks/useKLineWithRealTime';
import { KLineData } from '../../types';

const Container = styled.div`
  height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  background: #0b111a;
  color: #e6edf3;
`;

const ChartHeader = styled.div`
  min-height: 48px;
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 10px;
  align-items: center;
  padding: 6px 10px;
  border-bottom: 1px solid rgba(87, 100, 122, 0.34);
  background: rgba(13, 19, 29, 0.86);

  @media (max-width: 720px) {
    grid-template-columns: 1fr;
  }
`;

const MarketBlock = styled.div`
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
`;

const ChartIcon = styled.div`
  width: 28px;
  height: 28px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 7px;
  color: #79c0ff;
  background: linear-gradient(145deg, rgba(56, 139, 253, 0.2), rgba(46, 160, 67, 0.08));
  border: 1px solid rgba(88, 166, 255, 0.28);
`;

const SymbolStack = styled.div`
  display: grid;
  gap: 2px;
  min-width: 0;
`;

const SymbolName = styled.div`
  font-size: 11px;
  color: #8b949e;
  font-weight: 700;
  letter-spacing: 0.04em;
`;

const PriceLine = styled.div<{ positive: boolean }>`
  display: flex;
  align-items: baseline;
  gap: 10px;
  color: ${({ positive }) => (positive ? '#26a69a' : '#ef5350')};
`;

const LastPrice = styled.div`
  font-size: 18px;
  line-height: 1;
  font-weight: 800;
  font-variant-numeric: tabular-nums;
`;

const ChangeText = styled.div`
  font-size: 11px;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
`;

const Metrics = styled.div`
  display: flex;
  flex-wrap: wrap;
  gap: 6px 12px;
`;

const Metric = styled.div`
  display: grid;
  gap: 1px;
  min-width: 60px;
`;

const MetricLabel = styled.span`
  font-size: 10px;
  color: #6e7681;
`;

const MetricValue = styled.span`
  font-size: 11px;
  color: #c9d1d9;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
`;

const Controls = styled.div`
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 8px;
  flex-wrap: wrap;
`;

const StatusPill = styled.div<{ connected: boolean }>`
  height: 26px;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 0 8px;
  border-radius: 7px;
  font-size: 11px;
  font-weight: 700;
  color: ${({ connected }) => (connected ? '#3fb950' : '#8b949e')};
  border: 1px solid ${({ connected }) => (connected ? 'rgba(63, 185, 80, 0.34)' : 'rgba(139, 148, 158, 0.28)')};
  background: ${({ connected }) => (connected ? 'rgba(35, 134, 54, 0.12)' : 'rgba(139, 148, 158, 0.08)')};
`;

const TimeframeSelector = styled.div`
  display: flex;
  gap: 3px;
  padding: 2px;
  border-radius: 7px;
  background: #0b111a;
  border: 1px solid rgba(87, 100, 122, 0.34);
`;

const TimeframeButton = styled.button<{ active: boolean }>`
  height: 22px;
  min-width: 32px;
  padding: 0 7px;
  border: 0;
  border-radius: 5px;
  background: ${({ active }) => (active ? '#f0b90b' : 'transparent')};
  color: ${({ active }) => (active ? '#111820' : '#8b949e')};
  font-size: 11px;
  font-weight: 800;
  cursor: pointer;

  &:hover {
    color: ${({ active }) => (active ? '#111820' : '#f0b90b')};
  }
`;

const ResetButton = styled.button`
  width: 26px;
  height: 26px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 7px;
  border: 1px solid rgba(87, 100, 122, 0.38);
  color: #8b949e;
  background: #0b111a;
  cursor: pointer;

  &:hover {
    color: #f0f6fc;
    border-color: #58a6ff;
  }
`;

const FullscreenButton = styled(ResetButton)``;

const ChartContainer = styled.div`
  flex: 1;
  min-height: 0;
  position: relative;
`;

const LoadingOverlay = styled.div`
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 4;
  color: #8b949e;
  background: rgba(9, 13, 19, 0.72);
  backdrop-filter: blur(2px);
  font-size: 13px;
`;

interface ProfessionalKLineChartProps {
  symbol: string;
  timeframe: string;
  onTimeframeChange: (timeframe: string) => void;
}

const timeframes = ['1m', '5m', '15m', '1h', '4h', '1d', '1w'];

const formatNumber = (value: number, digits = 2) => {
  if (!Number.isFinite(value)) return '--';
  return value.toLocaleString('en-US', {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  });
};

const formatCompact = (value: number) => {
  if (!Number.isFinite(value)) return '--';
  return Intl.NumberFormat('en-US', { notation: 'compact', maximumFractionDigits: 2 }).format(value);
};

const UP_COLOR = '#26a69a';
const DOWN_COLOR = '#ef5350';
const MA5_COLOR = '#f9a825';
const MA10_COLOR = '#42a5f5';
const MA30_COLOR = '#ce93d8';

const CHART_BG = '#0d1117';
const VERT_GRID = 'rgba(48, 54, 61, 0.6)';
const HORZ_GRID = 'rgba(48, 54, 61, 0.6)';
const TEXT_COLOR = '#8b949e';
const CROSSHAIR_COLOR = 'rgba(255,255,255,0.5)';

export const ProfessionalKLineChart: React.FC<ProfessionalKLineChartProps> = React.memo(({
  symbol,
  timeframe,
  onTimeframeChange,
}) => {
  const { data: klineData, loading, error, isConnected, lastUpdate } = useKLineWithRealTime({ symbol, timeframe });
  const chartContainerRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<IChartApi | null>(null);
  const candleSeriesRef = useRef<ISeriesApi<SeriesType> | null>(null);
  const volumeSeriesRef = useRef<ISeriesApi<SeriesType> | null>(null);
  const ma5SeriesRef = useRef<ISeriesApi<SeriesType> | null>(null);
  const ma10SeriesRef = useRef<ISeriesApi<SeriesType> | null>(null);
  const ma30SeriesRef = useRef<ISeriesApi<SeriesType> | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  const data = useMemo(() => klineData ?? [], [klineData]);

  const stats = useMemo(() => {
    if (data.length === 0) return null;
    const latest = data[data.length - 1];
    const previous = data[data.length - 2] ?? latest;
    const change = latest.close - previous.close;
    const changePercent = previous.close ? (change / previous.close) * 100 : 0;
    return {
      price: latest.close,
      change,
      changePercent,
      high: Math.max(...data.map(item => item.high)),
      low: Math.min(...data.map(item => item.low)),
      volume: data.reduce((sum, item) => sum + item.volume, 0),
    };
  }, [data]);

  // Dedup by SECONDS (lightweight-charts Time is seconds, need unique sec-level keys)
  const deduped = useMemo(() => {
    const seen = new Map<number, KLineData>();
    for (const item of data) {
      const sec = Math.floor(item.timestamp / 1000);
      seen.set(sec, item); // keep latest per second
    }
    return Array.from(seen.values()).sort((a, b) => a.timestamp - b.timestamp);
  }, [data]);

  // Convert data to lightweight-charts format
  const candleData = useMemo<CandlestickData[]>(() =>
    deduped.map(item => ({
      time: (item.timestamp / 1000) as Time,
      open: item.open,
      high: item.high,
      low: item.low,
      close: item.close,
    })), [deduped]);

  const volumeData = useMemo<HistogramData[]>(() =>
    deduped.map(item => ({
      time: (item.timestamp / 1000) as Time,
      value: item.volume,
      color: item.close >= item.open ? 'rgba(38, 166, 154, 0.55)' : 'rgba(239, 83, 80, 0.55)',
    })), [deduped]);

  const ma5Data = useMemo<LineData[]>(() => calcMA(deduped, 5), [deduped]);
  const ma10Data = useMemo<LineData[]>(() => calcMA(deduped, 10), [deduped]);
  const ma30Data = useMemo<LineData[]>(() => calcMA(deduped, 30), [deduped]);

  // Initialize chart
  useEffect(() => {
    if (!chartContainerRef.current) return;

    const chart = createChart(chartContainerRef.current, {
      layout: {
        background: { type: ColorType.Solid, color: CHART_BG },
        textColor: TEXT_COLOR,
        fontSize: 11,
        fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif',
      },
      grid: {
        vertLines: { color: VERT_GRID },
        horzLines: { color: HORZ_GRID },
      },
      crosshair: {
        mode: CrosshairMode.Normal,
        vertLine: {
          color: CROSSHAIR_COLOR,
          width: 1,
          labelBackgroundColor: '#21262d',
        },
        horzLine: {
          color: CROSSHAIR_COLOR,
          width: 1,
          labelBackgroundColor: '#21262d',
        },
      },
      rightPriceScale: {
        borderColor: 'rgba(48, 54, 61, 0.8)',
        scaleMargins: { top: 0.05, bottom: 0.2 },
        autoScale: true,
      },
      timeScale: {
        borderColor: 'rgba(48, 54, 61, 0.8)',
        timeVisible: true,
        secondsVisible: false,
        rightOffset: 6,
        barSpacing: 6,
        minBarSpacing: 2,
      },
      handleScroll: { vertTouchDrag: false },
      localization: {
        locale: 'zh-CN',
        timeFormatter: (time: Time) => {
          const ts = (time as number) * 1000;
          return new Date(ts).toLocaleString('zh-CN', {
            month: '2-digit',
            day: '2-digit',
            hour: '2-digit',
            minute: '2-digit',
            hour12: false,
            timeZone: 'Asia/Shanghai',
          });
        },
      },
    });

    const candleSeries = chart.addSeries(CandlestickSeries, {
      upColor: UP_COLOR,
      downColor: DOWN_COLOR,
      borderColor: 'rgba(255,255,255,0.15)',
      wickColor: 'rgba(255,255,255,0.5)',
      priceLineVisible: false,
      lastValueVisible: false,
    });

    candleSeries.priceScale().applyOptions({
      scaleMargins: { top: 0.08, bottom: 0.25 },
    });

    const volumeSeries = chart.addSeries(HistogramSeries, {
      priceFormat: { type: 'volume' },
      priceScaleId: '',
    });
    volumeSeries.priceScale().applyOptions({
      scaleMargins: { top: 0.85, bottom: 0 },
    });

    const ma5Line = chart.addSeries(LineSeries, {
      color: MA5_COLOR,
      lineWidth: 2,
      priceLineVisible: false,
      lastValueVisible: true,
      crosshairMarkerVisible: false,
    });

    const ma10Line = chart.addSeries(LineSeries, {
      color: MA10_COLOR,
      lineWidth: 2,
      priceLineVisible: false,
      lastValueVisible: true,
      crosshairMarkerVisible: false,
    });

    const ma30Line = chart.addSeries(LineSeries, {
      color: MA30_COLOR,
      lineWidth: 2,
      priceLineVisible: false,
      lastValueVisible: true,
      crosshairMarkerVisible: false,
    });

    chartRef.current = chart;
    candleSeriesRef.current = candleSeries;
    volumeSeriesRef.current = volumeSeries;
    ma5SeriesRef.current = ma5Line;
    ma10SeriesRef.current = ma10Line;
    ma30SeriesRef.current = ma30Line;

    const handleResize = () => {
      if (chartContainerRef.current) {
        chart.applyOptions({
          width: chartContainerRef.current.clientWidth,
          height: chartContainerRef.current.clientHeight,
        });
      }
    };

    handleResize();
    const ro = new ResizeObserver(handleResize);
    ro.observe(chartContainerRef.current);

    return () => {
      ro.disconnect();
      chart.remove();
      chartRef.current = null;
    };
  }, []); // Only create chart once

  // Update data (with per-series dedup to prevent duplicate Time errors)
  useEffect(() => {
    const uniqueByTime = <T extends { time: Time }>(arr: T[]): T[] => {
      const seen = new Set<number>();
      return arr.filter(item => {
        const t = item.time as number;
        if (seen.has(t)) return false;
        seen.add(t);
        return true;
      });
    };

    if (candleSeriesRef.current) candleSeriesRef.current.setData(uniqueByTime(candleData));
    if (volumeSeriesRef.current) volumeSeriesRef.current.setData(uniqueByTime(volumeData));
    if (ma5SeriesRef.current) ma5SeriesRef.current.setData(uniqueByTime(ma5Data));
    if (ma10SeriesRef.current) ma10SeriesRef.current.setData(uniqueByTime(ma10Data));
    if (ma30SeriesRef.current) ma30SeriesRef.current.setData(uniqueByTime(ma30Data));
  }, [candleData, volumeData, ma5Data, ma10Data, ma30Data]);

  const resetView = () => {
    const chart = chartRef.current;
    if (chart) chart.timeScale().fitContent();
  };

  const toggleFullscreen = () => {
    const el = containerRef.current;
    if (!el) return;
    if (document.fullscreenElement) {
      document.exitFullscreen();
    } else {
      el.requestFullscreen();
    }
  };

  const positive = (stats?.changePercent ?? 0) >= 0;

  return (
    <Container ref={containerRef}>
      <ChartHeader>
        <MarketBlock>
          <ChartIcon>
            <BarChart3 size={18} />
          </ChartIcon>
          <SymbolStack>
            <SymbolName>{symbol} / {timeframe}</SymbolName>
            <PriceLine positive={positive}>
              <LastPrice>{stats ? formatNumber(stats.price, stats.price > 10 ? 2 : 4) : '--'}</LastPrice>
              <ChangeText>
                {stats ? `${stats.change >= 0 ? '+' : ''}${formatNumber(stats.change, 2)} (${stats.changePercent >= 0 ? '+' : ''}${formatNumber(stats.changePercent, 2)}%)` : '--'}
              </ChangeText>
            </PriceLine>
          </SymbolStack>
          <Metrics>
            <Metric>
              <MetricLabel>24h高</MetricLabel>
              <MetricValue>{stats ? formatNumber(stats.high, 2) : '--'}</MetricValue>
            </Metric>
            <Metric>
              <MetricLabel>24h低</MetricLabel>
              <MetricValue>{stats ? formatNumber(stats.low, 2) : '--'}</MetricValue>
            </Metric>
            <Metric>
              <MetricLabel>成交量</MetricLabel>
              <MetricValue>{stats ? formatCompact(stats.volume) : '--'}</MetricValue>
            </Metric>
            <Metric>
              <MetricLabel>更新</MetricLabel>
              <MetricValue>{lastUpdate > 0 ? new Date(lastUpdate).toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit', hour12: false, timeZone: 'Asia/Shanghai' }) : '--'}</MetricValue>
            </Metric>
          </Metrics>
        </MarketBlock>

        <Controls>
          <StatusPill connected={isConnected}>
            {isConnected ? <Wifi size={14} /> : <WifiOff size={14} />}
            {isConnected ? '实时' : '离线'}
          </StatusPill>
          <ResetButton type="button" onClick={resetView} title="重置视图">
            <Maximize2 size={15} />
          </ResetButton>
          <FullscreenButton type="button" onClick={toggleFullscreen} title="全屏">
            <Maximize2 size={15} />
          </FullscreenButton>
          <TimeframeSelector>
            {timeframes.map(value => (
              <TimeframeButton
                key={value}
                active={timeframe === value}
                type="button"
                onClick={() => onTimeframeChange(value)}
              >
                {value}
              </TimeframeButton>
            ))}
          </TimeframeSelector>
        </Controls>
      </ChartHeader>

      <ChartContainer ref={chartContainerRef}>
        {(loading || error || data.length === 0) && (
          <LoadingOverlay>
            {loading ? '正在加载 K 线数据...' : error ? 'K 线数据暂不可用' : '暂无历史 K 线，等待实时数据补齐'}
          </LoadingOverlay>
        )}
      </ChartContainer>
    </Container>
  );
});

function calcMA(data: KLineData[], period: number): LineData[] {
  const result: LineData[] = [];
  let sum = 0;
  for (let i = 0; i < data.length; i++) {
    sum += data[i].close;
    if (i >= period) sum -= data[i - period].close;
    if (i >= period - 1) {
      result.push({
        time: (data[i].timestamp / 1000) as Time,
        value: sum / period,
      });
    }
  }
  return result;
}
