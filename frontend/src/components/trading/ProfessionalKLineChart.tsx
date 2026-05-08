import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import styled from 'styled-components';
import { BarChart3, Maximize2, Wifi, WifiOff } from 'lucide-react';
import { useKLineWithRealTime } from '../../hooks/useKLineWithRealTime';
import { KLineData } from '../../types';

const PRICE_AXIS_WIDTH = 86;
const CHART_PADDING_TOP = 24;
const CHART_PADDING_BOTTOM = 26;
const VOLUME_PADDING_TOP = 14;
const VOLUME_PADDING_BOTTOM = 18;

const Container = styled.div`
  height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  background:
    linear-gradient(180deg, rgba(17, 24, 35, 0.98), rgba(10, 15, 23, 0.98)),
    #0b111a;
  color: #e6edf3;
`;

const ChartHeader = styled.div`
  min-height: 52px;
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 10px;
  align-items: center;
  padding: 7px 10px;
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
  width: 30px;
  height: 30px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 8px;
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
  font-size: 12px;
  color: #8b949e;
  font-weight: 700;
  letter-spacing: 0.04em;
`;

const PriceLine = styled.div<{ positive: boolean }>`
  display: flex;
  align-items: baseline;
  gap: 10px;
  color: ${({ positive }) => (positive ? '#3fb950' : '#f85149')};
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
  gap: 8px 14px;
`;

const Metric = styled.div`
  display: grid;
  gap: 2px;
  min-width: 70px;
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
  gap: 4px;
  padding: 3px;
  border-radius: 7px;
  background: #0b111a;
  border: 1px solid rgba(87, 100, 122, 0.34);
`;

const TimeframeButton = styled.button<{ active: boolean }>`
  height: 22px;
  min-width: 34px;
  padding: 0 8px;
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

const ChartArea = styled.div`
  flex: 1;
  min-height: 0;
  position: relative;
  display: grid;
  grid-template-rows: minmax(0, 72%) minmax(92px, 28%);
  cursor: crosshair;
  background:
    linear-gradient(90deg, rgba(88, 166, 255, 0.05) 1px, transparent 1px),
    linear-gradient(180deg, rgba(139, 148, 158, 0.04) 1px, transparent 1px);
  background-size: 96px 100%, 100% 54px;
`;

const CanvasLayer = styled.canvas`
  width: 100%;
  height: 100%;
  display: block;
`;

const PriceChart = styled.div`
  min-height: 0;
  position: relative;
  border-bottom: 1px solid rgba(87, 100, 122, 0.34);
`;

const VolumeChart = styled.div`
  min-height: 0;
  position: relative;
`;

const Legend = styled.div`
  position: absolute;
  left: 14px;
  top: 10px;
  display: flex;
  gap: 14px;
  z-index: 2;
  pointer-events: none;
  font-size: 11px;
  font-weight: 800;
`;

const LegendItem = styled.span<{ color: string }>`
  color: ${({ color }) => color};
`;

const Tooltip = styled.div<{ visible: boolean; x: number; y: number }>`
  position: absolute;
  left: ${({ x }) => x}px;
  top: ${({ y }) => y}px;
  display: ${({ visible }) => (visible ? 'grid' : 'none')};
  gap: 5px;
  width: 172px;
  padding: 10px;
  z-index: 5;
  pointer-events: none;
  border-radius: 8px;
  border: 1px solid rgba(88, 166, 255, 0.28);
  background: rgba(13, 19, 29, 0.96);
  box-shadow: 0 18px 36px rgba(0, 0, 0, 0.36);
`;

const TooltipRow = styled.div`
  display: flex;
  justify-content: space-between;
  gap: 10px;
  font-size: 12px;
`;

const TooltipLabel = styled.span`
  color: #8b949e;
`;

const TooltipValue = styled.span`
  color: #f0f6fc;
  font-weight: 800;
  font-variant-numeric: tabular-nums;
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
`;

interface ProfessionalKLineChartProps {
  symbol: string;
  timeframe: string;
  onTimeframeChange: (timeframe: string) => void;
}

const timeframes = ['1m', '5m', '15m', '1h', '4h', '1d', '1w'];

const colors = {
  up: '#2ea043',
  upBright: '#3fb950',
  down: '#da3633',
  downBright: '#f85149',
  grid: 'rgba(139, 148, 158, 0.14)',
  axis: '#6e7681',
  text: '#c9d1d9',
  ma5: '#f0b90b',
  ma10: '#58a6ff',
  ma30: '#d2a8ff',
};

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

const getIntervalMs = (timeframe: string) => {
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

const calculateMA = (data: KLineData[], period: number) => {
  const result = new Array<number | null>(data.length).fill(null);
  let rolling = 0;
  data.forEach((item, index) => {
    rolling += item.close;
    if (index >= period) rolling -= data[index - period].close;
    if (index >= period - 1) result[index] = rolling / period;
  });
  return result;
};

const resizeCanvas = (canvas: HTMLCanvasElement) => {
  const rect = canvas.getBoundingClientRect();
  const ratio = window.devicePixelRatio || 1;
  canvas.width = Math.max(1, Math.floor(rect.width * ratio));
  canvas.height = Math.max(1, Math.floor(rect.height * ratio));
  const ctx = canvas.getContext('2d');
  if (!ctx) return null;
  ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
  return { ctx, width: rect.width, height: rect.height };
};

const drawRoundRect = (
  ctx: CanvasRenderingContext2D,
  x: number,
  y: number,
  width: number,
  height: number,
  radius: number
) => {
  const r = Math.min(radius, Math.abs(width) / 2, Math.abs(height) / 2);
  ctx.beginPath();
  ctx.moveTo(x + r, y);
  ctx.lineTo(x + width - r, y);
  ctx.quadraticCurveTo(x + width, y, x + width, y + r);
  ctx.lineTo(x + width, y + height - r);
  ctx.quadraticCurveTo(x + width, y + height, x + width - r, y + height);
  ctx.lineTo(x + r, y + height);
  ctx.quadraticCurveTo(x, y + height, x, y + height - r);
  ctx.lineTo(x, y + r);
  ctx.quadraticCurveTo(x, y, x + r, y);
  ctx.closePath();
};

export const ProfessionalKLineChart: React.FC<ProfessionalKLineChartProps> = ({
  symbol,
  timeframe,
  onTimeframeChange,
}) => {
  const { data: klineData, loading, error, isConnected, lastUpdate } = useKLineWithRealTime({ symbol, timeframe });
  const [zoomLevel, setZoomLevel] = useState(1);
  const [panOffset, setPanOffset] = useState(0);
  const [hoverIndex, setHoverIndex] = useState<number | null>(null);
  const [tooltip, setTooltip] = useState({ visible: false, x: 0, y: 0 });
  const [dragStart, setDragStart] = useState<{ x: number; pan: number } | null>(null);
  const priceCanvasRef = useRef<HTMLCanvasElement>(null);
  const volumeCanvasRef = useRef<HTMLCanvasElement>(null);
  const chartAreaRef = useRef<HTMLDivElement>(null);

  const data = useMemo(() => klineData ?? [], [klineData]);
  const ma5 = useMemo(() => calculateMA(data, 5), [data]);
  const ma10 = useMemo(() => calculateMA(data, 10), [data]);
  const ma30 = useMemo(() => calculateMA(data, 30), [data]);
  const intervalMs = useMemo(() => getIntervalMs(timeframe), [timeframe]);

  const visibleMeta = useMemo(() => {
    const total = data.length;
    if (total === 0) {
      return {
        visible: [] as KLineData[],
        startIndex: 0,
        endIndex: 0,
        visibleCount: 0,
        slotCount: 48,
        rangeStart: 0,
      };
    }

    const slotCount = Math.max(36, Math.round(96 / zoomLevel));
    const firstTimestamp = data[0].timestamp;
    const lastTimestamp = data[total - 1].timestamp;
    const totalSlots = Math.max(total, Math.round((lastTimestamp - firstTimestamp) / intervalMs) + 1);
    const maxPan = Math.max(0, totalSlots - slotCount);
    const clampedPan = Math.max(0, Math.min(panOffset, maxPan));
    const endSlot = totalSlots - clampedPan;
    const startSlot = Math.max(0, endSlot - slotCount);
    const rangeStart = firstTimestamp + startSlot * intervalMs;
    const rangeEnd = rangeStart + slotCount * intervalMs;
    const startIndex = data.findIndex(item => item.timestamp >= rangeStart);
    const normalizedStartIndex = startIndex >= 0 ? startIndex : total;
    const endIndex = (() => {
      const index = data.findIndex(item => item.timestamp >= rangeEnd);
      return index >= 0 ? index : total;
    })();

    return {
      visible: data.slice(normalizedStartIndex, endIndex),
      startIndex: normalizedStartIndex,
      endIndex,
      visibleCount: slotCount,
      slotCount,
      rangeStart,
    };
  }, [data, intervalMs, panOffset, zoomLevel]);

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

  const hoveredCandle = hoverIndex !== null ? data[hoverIndex] : null;
  const latestMa = {
    ma5: ma5[ma5.length - 1],
    ma10: ma10[ma10.length - 1],
    ma30: ma30[ma30.length - 1],
  };

  const drawPriceChart = useCallback(() => {
    const canvas = priceCanvasRef.current;
    if (!canvas) return;
    const sized = resizeCanvas(canvas);
    if (!sized) return;
    const { ctx, width, height } = sized;
    ctx.clearRect(0, 0, width, height);

    const { visible, startIndex, slotCount, rangeStart } = visibleMeta;
    const chartWidth = Math.max(1, width - PRICE_AXIS_WIDTH);
    const chartHeight = Math.max(1, height - CHART_PADDING_TOP - CHART_PADDING_BOTTOM);
    const chartBottom = CHART_PADDING_TOP + chartHeight;

    const gradient = ctx.createLinearGradient(0, 0, 0, height);
    gradient.addColorStop(0, 'rgba(88, 166, 255, 0.08)');
    gradient.addColorStop(1, 'rgba(13, 19, 29, 0)');
    ctx.fillStyle = gradient;
    ctx.fillRect(0, 0, chartWidth, height);

    if (visible.length === 0) return;

    const maxPrice = Math.max(...visible.map(item => item.high));
    const minPrice = Math.min(...visible.map(item => item.low));
    const range = Math.max(0.00000001, maxPrice - minPrice);
    const topPrice = maxPrice + range * 0.08;
    const bottomPrice = minPrice - range * 0.08;
    const priceRange = topPrice - bottomPrice;
    const candleStep = chartWidth / Math.max(1, slotCount);
    const candleWidth = Math.max(3, Math.min(12, candleStep * 0.64));
    const xForTimestamp = (timestamp: number) => {
      const slotIndex = Math.round((timestamp - rangeStart) / intervalMs);
      return slotIndex * candleStep + candleStep / 2;
    };
    const yFor = (price: number) => CHART_PADDING_TOP + (topPrice - price) / priceRange * chartHeight;

    ctx.strokeStyle = colors.grid;
    ctx.lineWidth = 1;
    ctx.setLineDash([4, 6]);
    for (let i = 0; i <= 4; i += 1) {
      const y = CHART_PADDING_TOP + (chartHeight / 4) * i;
      ctx.beginPath();
      ctx.moveTo(0, y);
      ctx.lineTo(chartWidth, y);
      ctx.stroke();
    }
    for (let i = 0; i <= 6; i += 1) {
      const x = (chartWidth / 6) * i;
      ctx.beginPath();
      ctx.moveTo(x, CHART_PADDING_TOP);
      ctx.lineTo(x, chartBottom);
      ctx.stroke();
    }
    ctx.setLineDash([]);

    const drawMaLine = (values: Array<number | null>, color: string) => {
      ctx.strokeStyle = color;
      ctx.lineWidth = 1.6;
      ctx.beginPath();
      let started = false;
      visible.forEach((_, localIndex) => {
        const value = values[startIndex + localIndex];
        if (!value) return;
        const x = xForTimestamp(visible[localIndex].timestamp);
        const y = yFor(value);
        if (!started) {
          ctx.moveTo(x, y);
          started = true;
        } else {
          ctx.lineTo(x, y);
        }
      });
      ctx.stroke();
    };

    visible.forEach(candle => {
      const x = xForTimestamp(candle.timestamp);
      const openY = yFor(candle.open);
      const closeY = yFor(candle.close);
      const highY = yFor(candle.high);
      const lowY = yFor(candle.low);
      const isUp = candle.close >= candle.open;
      const candleColor = isUp ? colors.upBright : colors.downBright;

      ctx.strokeStyle = candleColor;
      ctx.lineWidth = 1.2;
      ctx.beginPath();
      ctx.moveTo(x, highY);
      ctx.lineTo(x, lowY);
      ctx.stroke();

      const bodyTop = Math.min(openY, closeY);
      const bodyHeight = Math.max(2, Math.abs(closeY - openY));
      ctx.fillStyle = candleColor;
      drawRoundRect(ctx, x - candleWidth / 2, bodyTop, candleWidth, bodyHeight, 2);
      ctx.fill();
    });

    drawMaLine(ma5, colors.ma5);
    drawMaLine(ma10, colors.ma10);
    drawMaLine(ma30, colors.ma30);

    if (hoverIndex !== null && hoverIndex >= startIndex && hoverIndex < startIndex + visible.length) {
      const candle = data[hoverIndex];
      const x = xForTimestamp(candle.timestamp);
      const y = yFor(candle.close);
      ctx.strokeStyle = 'rgba(201, 209, 217, 0.36)';
      ctx.lineWidth = 1;
      ctx.setLineDash([5, 5]);
      ctx.beginPath();
      ctx.moveTo(x, CHART_PADDING_TOP);
      ctx.lineTo(x, chartBottom);
      ctx.moveTo(0, y);
      ctx.lineTo(chartWidth, y);
      ctx.stroke();
      ctx.setLineDash([]);
    }

    ctx.fillStyle = colors.axis;
    ctx.font = '12px -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif';
    ctx.textAlign = 'right';
    ctx.textBaseline = 'middle';
    for (let i = 0; i <= 4; i += 1) {
      const price = topPrice - (priceRange / 4) * i;
      const y = CHART_PADDING_TOP + (chartHeight / 4) * i;
      ctx.fillText(formatNumber(price, price > 10 ? 2 : 4), width - 8, y);
    }

    ctx.fillStyle = 'rgba(139, 148, 158, 0.5)';
    ctx.fillRect(chartWidth, 0, 1, height);

    const timeSamples = [0, Math.floor((slotCount - 1) / 2), slotCount - 1]
      .filter((value, index, array) => value >= 0 && array.indexOf(value) === index);
    ctx.textAlign = 'center';
    ctx.textBaseline = 'bottom';
    timeSamples.forEach(slotIndex => {
      const x = Math.min(chartWidth - 28, Math.max(28, slotIndex * candleStep + candleStep / 2));
      ctx.fillText(formatTime(rangeStart + slotIndex * intervalMs), x, height - 6);
    });
  }, [data, hoverIndex, intervalMs, ma5, ma10, ma30, visibleMeta]);

  const drawVolumeChart = useCallback(() => {
    const canvas = volumeCanvasRef.current;
    if (!canvas) return;
    const sized = resizeCanvas(canvas);
    if (!sized) return;
    const { ctx, width, height } = sized;
    ctx.clearRect(0, 0, width, height);

    const { visible, startIndex, slotCount, rangeStart } = visibleMeta;
    const chartWidth = Math.max(1, width - PRICE_AXIS_WIDTH);
    const chartHeight = Math.max(1, height - VOLUME_PADDING_TOP - VOLUME_PADDING_BOTTOM);
    const chartBottom = VOLUME_PADDING_TOP + chartHeight;
    if (visible.length === 0) return;

    const maxVolume = Math.max(...visible.map(item => item.volume), 1);
    const candleStep = chartWidth / Math.max(1, slotCount);
    const barWidth = Math.max(3, Math.min(12, candleStep * 0.64));
    const maVolume = calculateVolumeMA(data, 20);

    ctx.strokeStyle = colors.grid;
    ctx.lineWidth = 1;
    ctx.setLineDash([4, 6]);
    for (let i = 0; i <= 2; i += 1) {
      const y = VOLUME_PADDING_TOP + (chartHeight / 2) * i;
      ctx.beginPath();
      ctx.moveTo(0, y);
      ctx.lineTo(chartWidth, y);
      ctx.stroke();
    }
    ctx.setLineDash([]);

    visible.forEach(candle => {
      const slotIndex = Math.round((candle.timestamp - rangeStart) / intervalMs);
      const x = slotIndex * candleStep + candleStep / 2;
      const barHeight = Math.max(2, candle.volume / maxVolume * chartHeight);
      const y = chartBottom - barHeight;
      const isUp = candle.close >= candle.open;
      const gradient = ctx.createLinearGradient(0, y, 0, chartBottom);
      gradient.addColorStop(0, isUp ? 'rgba(63, 185, 80, 0.92)' : 'rgba(248, 81, 73, 0.92)');
      gradient.addColorStop(1, isUp ? 'rgba(63, 185, 80, 0.2)' : 'rgba(248, 81, 73, 0.2)');
      ctx.fillStyle = gradient;
      drawRoundRect(ctx, x - barWidth / 2, y, barWidth, barHeight, 2);
      ctx.fill();
    });

    ctx.strokeStyle = '#f0b90b';
    ctx.lineWidth = 1.4;
    ctx.beginPath();
    let started = false;
    visible.forEach((_, localIndex) => {
      const value = maVolume[startIndex + localIndex];
      if (!value) return;
      const slotIndex = Math.round((visible[localIndex].timestamp - rangeStart) / intervalMs);
      const x = slotIndex * candleStep + candleStep / 2;
      const y = chartBottom - value / maxVolume * chartHeight;
      if (!started) {
        ctx.moveTo(x, y);
        started = true;
      } else {
        ctx.lineTo(x, y);
      }
    });
    ctx.stroke();

    if (hoverIndex !== null && hoverIndex >= startIndex && hoverIndex < startIndex + visible.length) {
      const candle = data[hoverIndex];
      const slotIndex = Math.round((candle.timestamp - rangeStart) / intervalMs);
      const x = slotIndex * candleStep + candleStep / 2;
      ctx.strokeStyle = 'rgba(201, 209, 217, 0.34)';
      ctx.setLineDash([5, 5]);
      ctx.beginPath();
      ctx.moveTo(x, VOLUME_PADDING_TOP);
      ctx.lineTo(x, chartBottom);
      ctx.stroke();
      ctx.setLineDash([]);
    }

    ctx.fillStyle = colors.axis;
    ctx.font = '11px -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif';
    ctx.textAlign = 'right';
    ctx.textBaseline = 'middle';
    ctx.fillText(formatCompact(maxVolume), width - 8, VOLUME_PADDING_TOP + 2);
    ctx.fillText('VOL', width - 8, height - 8);
    ctx.fillStyle = 'rgba(139, 148, 158, 0.5)';
    ctx.fillRect(chartWidth, 0, 1, height);
  }, [data, hoverIndex, intervalMs, visibleMeta]);

  useEffect(() => {
    drawPriceChart();
    drawVolumeChart();
  }, [drawPriceChart, drawVolumeChart]);

  useEffect(() => {
    const handleResize = () => {
      drawPriceChart();
      drawVolumeChart();
    };

    window.addEventListener('resize', handleResize);
    return () => window.removeEventListener('resize', handleResize);
  }, [drawPriceChart, drawVolumeChart]);

  useEffect(() => {
    setPanOffset(0);
    setHoverIndex(null);
  }, [symbol, timeframe]);

  const clampPan = useCallback((value: number, nextZoom = zoomLevel) => {
    const total = data.length;
    if (total <= 1) return 0;
    const totalSlots = Math.max(total, Math.round((data[total - 1].timestamp - data[0].timestamp) / intervalMs) + 1);
    const visibleCount = Math.max(36, Math.round(96 / nextZoom));
    const maxPan = Math.max(0, totalSlots - visibleCount);
    return Math.max(0, Math.min(value, maxPan));
  }, [data, intervalMs, zoomLevel]);

  const handleWheel = useCallback((event: React.WheelEvent<HTMLDivElement>) => {
    event.preventDefault();
    const nextZoom = Math.max(0.55, Math.min(4.2, zoomLevel * (event.deltaY > 0 ? 0.9 : 1.12)));
    setZoomLevel(nextZoom);
    setPanOffset(current => clampPan(current, nextZoom));
  }, [clampPan, zoomLevel]);

  const handleMouseMove = useCallback((event: React.MouseEvent<HTMLDivElement>) => {
    const area = chartAreaRef.current;
    if (!area || visibleMeta.visible.length === 0) return;
    const rect = area.getBoundingClientRect();
    const x = event.clientX - rect.left;
    const y = event.clientY - rect.top;
    const chartWidth = Math.max(1, rect.width - PRICE_AXIS_WIDTH);

    if (dragStart) {
      const candleStep = chartWidth / Math.max(1, visibleMeta.slotCount);
      const deltaSlots = Math.round((event.clientX - dragStart.x) / candleStep);
      setPanOffset(clampPan(dragStart.pan + deltaSlots));
      return;
    }

    if (x < 0 || x > chartWidth) {
      setHoverIndex(null);
      setTooltip({ visible: false, x: 0, y: 0 });
      return;
    }

    const slotWidth = chartWidth / Math.max(1, visibleMeta.slotCount);
    const slotIndex = Math.max(0, Math.min(visibleMeta.slotCount - 1, Math.floor(x / slotWidth)));
    const timestamp = visibleMeta.rangeStart + slotIndex * intervalMs;
    const globalIndex = data.findIndex(item => item.timestamp === timestamp);

    if (globalIndex < 0) {
      setHoverIndex(null);
      setTooltip({ visible: false, x: 0, y: 0 });
      return;
    }

    setHoverIndex(globalIndex);
    setTooltip({
      visible: true,
      x: Math.min(x + 14, rect.width - 190),
      y: Math.max(10, Math.min(y - 92, rect.height - 170)),
    });
  }, [clampPan, data, dragStart, intervalMs, visibleMeta]);

  const handleMouseDown = useCallback((event: React.MouseEvent<HTMLDivElement>) => {
    if (event.button !== 0) return;
    setDragStart({ x: event.clientX, pan: panOffset });
  }, [panOffset]);

  const handleMouseLeave = useCallback(() => {
    setHoverIndex(null);
    setTooltip({ visible: false, x: 0, y: 0 });
    setDragStart(null);
  }, []);

  const resetView = () => {
    setZoomLevel(1);
    setPanOffset(0);
  };

  const positive = (stats?.changePercent ?? 0) >= 0;

  return (
    <Container>
      <ChartHeader>
        <MarketBlock>
          <ChartIcon>
            <BarChart3 size={20} />
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
              <MetricLabel>24h 高</MetricLabel>
              <MetricValue>{stats ? formatNumber(stats.high, 2) : '--'}</MetricValue>
            </Metric>
            <Metric>
              <MetricLabel>24h 低</MetricLabel>
              <MetricValue>{stats ? formatNumber(stats.low, 2) : '--'}</MetricValue>
            </Metric>
            <Metric>
              <MetricLabel>成交量</MetricLabel>
              <MetricValue>{stats ? formatCompact(stats.volume) : '--'}</MetricValue>
            </Metric>
            <Metric>
              <MetricLabel>更新时间</MetricLabel>
              <MetricValue>{lastUpdate > 0 ? formatTime(lastUpdate) : '--'}</MetricValue>
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

      <ChartArea
        ref={chartAreaRef}
        onWheel={handleWheel}
        onMouseMove={handleMouseMove}
        onMouseDown={handleMouseDown}
        onMouseUp={() => setDragStart(null)}
        onMouseLeave={handleMouseLeave}
      >
        <PriceChart>
          <Legend>
            <LegendItem color={colors.ma5}>MA5 {latestMa.ma5 ? formatNumber(latestMa.ma5, 2) : '--'}</LegendItem>
            <LegendItem color={colors.ma10}>MA10 {latestMa.ma10 ? formatNumber(latestMa.ma10, 2) : '--'}</LegendItem>
            <LegendItem color={colors.ma30}>MA30 {latestMa.ma30 ? formatNumber(latestMa.ma30, 2) : '--'}</LegendItem>
          </Legend>
          <CanvasLayer ref={priceCanvasRef} />
        </PriceChart>
        <VolumeChart>
          <CanvasLayer ref={volumeCanvasRef} />
        </VolumeChart>

        {(loading || error || data.length === 0) && (
          <LoadingOverlay>
            {loading ? '正在加载 K 线数据...' : error ? 'K 线数据暂不可用' : '暂无历史 K 线，等待实时数据补齐'}
          </LoadingOverlay>
        )}

        <Tooltip visible={tooltip.visible} x={tooltip.x} y={tooltip.y}>
          {hoveredCandle && (
            <>
              <TooltipRow><TooltipLabel>时间</TooltipLabel><TooltipValue>{formatDateTime(hoveredCandle.timestamp)}</TooltipValue></TooltipRow>
              <TooltipRow><TooltipLabel>开盘</TooltipLabel><TooltipValue>{formatNumber(hoveredCandle.open, 2)}</TooltipValue></TooltipRow>
              <TooltipRow><TooltipLabel>最高</TooltipLabel><TooltipValue style={{ color: colors.upBright }}>{formatNumber(hoveredCandle.high, 2)}</TooltipValue></TooltipRow>
              <TooltipRow><TooltipLabel>最低</TooltipLabel><TooltipValue style={{ color: colors.downBright }}>{formatNumber(hoveredCandle.low, 2)}</TooltipValue></TooltipRow>
              <TooltipRow><TooltipLabel>收盘</TooltipLabel><TooltipValue>{formatNumber(hoveredCandle.close, 2)}</TooltipValue></TooltipRow>
              <TooltipRow><TooltipLabel>成交量</TooltipLabel><TooltipValue>{formatCompact(hoveredCandle.volume)}</TooltipValue></TooltipRow>
            </>
          )}
        </Tooltip>
      </ChartArea>
    </Container>
  );
};

const formatTime = (timestamp: number) => {
  return new Date(timestamp).toLocaleTimeString('zh-CN', {
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  });
};

const formatDateTime = (timestamp: number) => {
  return new Date(timestamp).toLocaleString('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  });
};

const calculateVolumeMA = (data: KLineData[], period: number) => {
  const result = new Array<number | null>(data.length).fill(null);
  let rolling = 0;
  data.forEach((item, index) => {
    rolling += item.volume;
    if (index >= period) rolling -= data[index - period].volume;
    if (index >= period - 1) result[index] = rolling / period;
  });
  return result;
};
