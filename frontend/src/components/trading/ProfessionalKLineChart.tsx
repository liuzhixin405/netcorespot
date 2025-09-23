import React, { useState, useEffect, useCallback, useRef } from 'react';
import styled from 'styled-components';
import { useKLineWithRealTime } from '../../hooks/useKLineWithRealTime';
import { KLineData } from '../../types';

const Container = styled.div`
  height: 100%;
  background: #0b0e11;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  border: 1px solid #2b3139;
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
`;

const ChartHeader = styled.div`
  padding: 12px 16px;
  border-bottom: 1px solid #2b3139;
  background: #161a1e;
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-size: 14px;
  font-weight: 600;
  color: #eaecef;
`;

const SymbolInfo = styled.div`
  display: flex;
  align-items: center;
  gap: 12px;
`;

const SymbolName = styled.div`
  font-size: 16px;
  font-weight: 700;
  color: #f0f6fc;
`;

const PriceDisplay = styled.div<{ isPositive: boolean }>`
  font-size: 24px;
  font-weight: 700;
  color: ${props => props.isPositive ? '#00d4aa' : '#f84960'};
`;

const ChangeDisplay = styled.div<{ isPositive: boolean }>`
  font-size: 14px;
  color: ${props => props.isPositive ? '#00d4aa' : '#f84960'};
  display: flex;
  flex-direction: column;
  align-items: flex-end;
`;

const TimeframeSelector = styled.div`
  display: flex;
  gap: 4px;
`;

const TimeframeButton = styled.button<{ active: boolean }>`
  padding: 6px 12px;
  border: 1px solid ${props => props.active ? '#f0b90b' : '#2b3139'};
  background: ${props => props.active ? '#f0b90b' : 'transparent'};
  color: ${props => props.active ? '#0b0e11' : '#848e9c'};
  border-radius: 4px;
  font-size: 12px;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s;
  
  &:hover {
    border-color: #f0b90b;
    color: ${props => props.active ? '#0b0e11' : '#f0b90b'};
  }
`;

const ChartContainer = styled.div`
  flex: 1;
  position: relative;
  overflow: hidden;
  background: #0b0e11;
`;

const PriceChart = styled.div`
  height: 70%;
  position: relative;
  border-bottom: 1px solid #2b3139;
`;

const VolumeChart = styled.div`
  height: 30%;
  position: relative;
`;

const Canvas = styled.canvas`
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
`;

const PriceAxis = styled.div`
  position: absolute;
  right: 0;
  top: 0;
  bottom: 0;
  width: 80px;
  border-left: 1px solid #2b3139;
  background: rgba(11, 14, 17, 0.8);
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  padding: 8px 4px;
  font-size: 12px;
  color: #848e9c;
`;

const TimeAxis = styled.div`
  position: absolute;
  bottom: 0;
  left: 0;
  right: 80px;
  height: 24px;
  border-top: 1px solid #2b3139;
  background: rgba(11, 14, 17, 0.8);
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0 8px;
  font-size: 12px;
  color: #848e9c;
`;

const MovingAverages = styled.div`
  position: absolute;
  top: 8px;
  left: 16px;
  display: flex;
  gap: 16px;
  font-size: 12px;
`;

const MAItem = styled.div<{ color: string }>`
  display: flex;
  align-items: center;
  gap: 4px;
  color: ${props => props.color};
  
  &::before {
    content: '';
    width: 12px;
    height: 2px;
    background: ${props => props.color};
  }
`;

const Tooltip = styled.div<{ visible: boolean; x: number; y: number }>`
  position: absolute;
  left: ${props => props.x}px;
  top: ${props => props.y}px;
  background: #1e2329;
  border: 1px solid #2b3139;
  border-radius: 4px;
  padding: 8px;
  font-size: 12px;
  color: #eaecef;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.4);
  z-index: 1000;
  display: ${props => props.visible ? 'block' : 'none'};
  min-width: 120px;
`;

const TooltipRow = styled.div`
  display: flex;
  justify-content: space-between;
  margin-bottom: 2px;
  
  &:last-child {
    margin-bottom: 0;
  }
`;

const TooltipLabel = styled.span`
  color: #848e9c;
  margin-right: 8px;
`;

const TooltipValue = styled.span`
  color: #eaecef;
  font-weight: 500;
`;

const LoadingOverlay = styled.div`
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(11, 14, 17, 0.8);
  display: flex;
  align-items: center;
  justify-content: center;
  color: #848e9c;
  font-size: 14px;
  z-index: 100;
`;

interface ProfessionalKLineChartProps {
  symbol: string;
  timeframe: string;
  onTimeframeChange: (timeframe: string) => void;
}

const timeframes = ['1m', '5m', '15m', '1h', '4h', '1d', '1w'];

export const ProfessionalKLineChart: React.FC<ProfessionalKLineChartProps> = ({
  symbol,
  timeframe,
  onTimeframeChange
}) => {
  const { data: klineData, loading: klineLoading, error: klineError, isConnected, lastUpdate, reconnect } = useKLineWithRealTime({ symbol, timeframe });
  const [tooltip, setTooltip] = useState<{
    visible: boolean;
    x: number;
    y: number;
    data: KLineData | null;
  }>({ visible: false, x: 0, y: 0, data: null });
  
  // 缩放和平移状态
  const [zoomLevel, setZoomLevel] = useState(1);
  const [panOffset, setPanOffset] = useState(0);
  const [isDragging, setIsDragging] = useState(false);
  const [dragStart, setDragStart] = useState({ x: 0, panStart: 0 });
  
  const priceCanvasRef = useRef<HTMLCanvasElement>(null);
  const volumeCanvasRef = useRef<HTMLCanvasElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  // 计算移动平均线
  const calculateMA = useCallback((data: KLineData[], period: number): number[] => {
    if (data.length < period) return [];
    
    const maValues: number[] = [];
    for (let i = period - 1; i < data.length; i++) {
      const sum = data.slice(i - period + 1, i + 1).reduce((acc, item) => acc + item.close, 0);
      maValues.push(sum / period);
    }
    return maValues;
  }, []);

  // 鼠标滚轮缩放处理
  const handleWheel = useCallback((event: React.WheelEvent) => {
    event.preventDefault();
    
    if (!klineData || klineData.length === 0) return;
    
    const rect = containerRef.current?.getBoundingClientRect();
    if (!rect) return;
    
    const mouseX = event.clientX - rect.left;
    const chartWidth = rect.width - 80; // 减去价格轴宽度
    
    // 计算鼠标位置对应的数据索引
    const baseCandleWidth = chartWidth / klineData.length;
    const scaledCandleWidth = baseCandleWidth * zoomLevel;
    const mouseDataIndex = Math.floor((mouseX + panOffset) / scaledCandleWidth);
    
    // 计算缩放因子
    const delta = event.deltaY > 0 ? 0.9 : 1.1;
    const newZoom = Math.max(0.5, Math.min(zoomLevel * delta, 10));
    
    // 计算新的平移偏移，保持鼠标位置对应的数据点不变
    const newScaledCandleWidth = baseCandleWidth * newZoom;
    const newPanOffset = mouseDataIndex * newScaledCandleWidth - mouseX;
    
    setZoomLevel(newZoom);
    setPanOffset(newPanOffset);
  }, [klineData, zoomLevel, panOffset]);

  // 鼠标拖拽平移处理
  const handleMouseDown = useCallback((event: React.MouseEvent) => {
    if (event.button === 0) { // 左键
      setIsDragging(true);
      setDragStart({ x: event.clientX, panStart: panOffset });
    }
  }, [panOffset]);

  const handleMouseMove = useCallback((event: React.MouseEvent) => {
    if (isDragging) {
      const deltaX = event.clientX - dragStart.x;
      setPanOffset(dragStart.panStart + deltaX);
    } else if (klineData && klineData.length > 0) {
      // 原有的悬停逻辑
      const rect = containerRef.current?.getBoundingClientRect();
      if (!rect) return;

      const x = event.clientX - rect.left;
      const y = event.clientY - rect.top;
      
      const baseCandleWidth = (rect.width - 80) / klineData.length;
      const scaledCandleWidth = baseCandleWidth * zoomLevel;
      const dataIndex = Math.floor((x + panOffset) / scaledCandleWidth);
      
      if (dataIndex >= 0 && dataIndex < klineData.length) {
        const candleData = klineData[dataIndex];
        setTooltip({
          visible: true,
          x: Math.min(x + 10, rect.width - 200),
          y: Math.max(y - 120, 10),
          data: candleData
        });
      }
    }
  }, [isDragging, dragStart, klineData, zoomLevel, panOffset]);

  const handleMouseUp = useCallback(() => {
    setIsDragging(false);
  }, []);

  const handleMouseLeave = useCallback(() => {
    setIsDragging(false);
    setTooltip({ visible: false, x: 0, y: 0, data: null });
  }, []);

  // 绘制K线图
  const drawPriceChart = useCallback(() => {
    const canvas = priceCanvasRef.current;
    if (!canvas || !klineData || klineData.length === 0) return;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const rect = canvas.getBoundingClientRect();
    canvas.width = rect.width * window.devicePixelRatio;
    canvas.height = rect.height * window.devicePixelRatio;
    ctx.scale(window.devicePixelRatio, window.devicePixelRatio);

    const width = rect.width;
    const height = rect.height;
    
    // 清除画布
    ctx.clearRect(0, 0, width, height);
    
    // 设置样式
    ctx.lineWidth = 1;
    ctx.font = '12px -apple-system, BlinkMacSystemFont, sans-serif';
    
    // 计算价格范围
    const prices = klineData.map((k: KLineData) => [k.high, k.low]).flat();
    const maxPrice = Math.max(...prices);
    const minPrice = Math.min(...prices);
    const priceRange = maxPrice - minPrice;
    const padding = priceRange * 0.1;
    const chartMinPrice = minPrice - padding;
    const chartMaxPrice = maxPrice + padding;
    const chartHeight = height - 40; // 留出顶部和底部空间
    const chartTop = 20;
    
    // 绘制网格线
    ctx.strokeStyle = '#2b3139';
    ctx.lineWidth = 0.5;
    
    // 水平网格线
    for (let i = 0; i <= 4; i++) {
      const y = chartTop + (chartHeight / 4) * i;
      ctx.beginPath();
      ctx.moveTo(0, y);
      ctx.lineTo(width - 80, y);
      ctx.stroke();
    }
    
    // 垂直网格线
    const baseCandleWidth = (width - 80) / klineData.length;
    const candleWidth = Math.max(1, baseCandleWidth * zoomLevel);
    for (let i = 0; i <= klineData.length; i += Math.max(1, Math.floor(klineData.length / 8))) {
      const x = i * candleWidth - panOffset;
      if (x >= -candleWidth && x < width - 80) {
        ctx.beginPath();
        ctx.moveTo(x, chartTop);
        ctx.lineTo(x, chartTop + chartHeight);
        ctx.stroke();
      }
    }
    
    // 绘制K线
    klineData.forEach((candle: KLineData, index: number) => {
      const x = index * candleWidth + candleWidth / 2 - panOffset;
      
      // 只绘制可见区域的K线
      if (x < -candleWidth || x > width - 80) return;
      
      const openY = chartTop + chartHeight - ((candle.open - chartMinPrice) / (chartMaxPrice - chartMinPrice)) * chartHeight;
      const closeY = chartTop + chartHeight - ((candle.close - chartMinPrice) / (chartMaxPrice - chartMinPrice)) * chartHeight;
      const highY = chartTop + chartHeight - ((candle.high - chartMinPrice) / (chartMaxPrice - chartMinPrice)) * chartHeight;
      const lowY = chartTop + chartHeight - ((candle.low - chartMinPrice) / (chartMaxPrice - chartMinPrice)) * chartHeight;
      
      const isGreen = candle.close >= candle.open;
      
      // 绘制影线
      ctx.strokeStyle = isGreen ? '#00d4aa' : '#f84960';
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.moveTo(x, highY);
      ctx.lineTo(x, lowY);
      ctx.stroke();
      
      // 绘制实体
      const bodyHeight = Math.abs(closeY - openY);
      const bodyTop = Math.min(openY, closeY);
      
      if (isGreen) {
        ctx.fillStyle = '#00d4aa';
        ctx.fillRect(x - candleWidth / 2 + 1, bodyTop, candleWidth - 2, bodyHeight || 1);
      } else {
        ctx.fillStyle = '#f84960';
        ctx.fillRect(x - candleWidth / 2 + 1, bodyTop, candleWidth - 2, bodyHeight || 1);
      }
    });
    
    // 绘制移动平均线
    if (klineData.length >= 5) {
      const ma5Values = calculateMA(klineData, 5);
      drawMALine(ctx, ma5Values, '#f0b90b', width, height, candleWidth, chartTop, chartHeight, chartMinPrice, chartMaxPrice);
    }
    
    if (klineData.length >= 10) {
      const ma10Values = calculateMA(klineData, 10);
      drawMALine(ctx, ma10Values, '#ff6b35', width, height, candleWidth, chartTop, chartHeight, chartMinPrice, chartMaxPrice);
    }
    
    if (klineData.length >= 30) {
      const ma30Values = calculateMA(klineData, 30);
      drawMALine(ctx, ma30Values, '#8b5cf6', width, height, candleWidth, chartTop, chartHeight, chartMinPrice, chartMaxPrice);
    }
    
    // 绘制价格标签
    drawPriceLabels(ctx, width, height, chartTop, chartHeight, chartMinPrice, chartMaxPrice);
  }, [klineData, calculateMA, zoomLevel, panOffset]);

  // 绘制移动平均线
  const drawMALine = (
    ctx: CanvasRenderingContext2D,
    maValues: number[],
    color: string,
    width: number,
    height: number,
    candleWidth: number,
    chartTop: number,
    chartHeight: number,
    chartMinPrice: number,
    chartMaxPrice: number
  ) => {
    ctx.strokeStyle = color;
    ctx.lineWidth = 1;
    ctx.beginPath();
    
    let firstPoint = true;
    maValues.forEach((value, index) => {
      const x = (index + maValues.length) * candleWidth + candleWidth / 2 - panOffset;
      
      // 只绘制可见区域的线段
      if (x >= -candleWidth && x <= width - 80) {
        const y = chartTop + chartHeight - ((value - chartMinPrice) / (chartMaxPrice - chartMinPrice)) * chartHeight;
        
        if (firstPoint) {
          ctx.moveTo(x, y);
          firstPoint = false;
        } else {
          ctx.lineTo(x, y);
        }
      }
    });
    
    ctx.stroke();
  };

  // 绘制价格标签
  const drawPriceLabels = (
    ctx: CanvasRenderingContext2D,
    width: number,
    height: number,
    chartTop: number,
    chartHeight: number,
    chartMinPrice: number,
    chartMaxPrice: number
  ) => {
    ctx.fillStyle = '#848e9c';
    ctx.textAlign = 'right';
    
    for (let i = 0; i <= 4; i++) {
      const price = chartMinPrice + (chartMaxPrice - chartMinPrice) * (1 - i / 4);
      const y = chartTop + (chartHeight / 4) * i + 4;
      
      ctx.fillText(price.toFixed(2), width - 80 + 4, y);
    }
  };

  // 绘制成交量图
  const drawVolumeChart = useCallback(() => {
    const canvas = volumeCanvasRef.current;
    if (!canvas || !klineData || klineData.length === 0) return;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const rect = canvas.getBoundingClientRect();
    canvas.width = rect.width * window.devicePixelRatio;
    canvas.height = rect.height * window.devicePixelRatio;
    ctx.scale(window.devicePixelRatio, window.devicePixelRatio);

    const width = rect.width;
    const height = rect.height;
    
    ctx.clearRect(0, 0, width, height);
    
    // 计算成交量范围
    const volumes = klineData.map((k: KLineData) => k.volume);
    const maxVolume = Math.max(...volumes);
    const baseCandleWidth = (width - 80) / klineData.length;
    const candleWidth = Math.max(1, baseCandleWidth * zoomLevel);
    
    // 绘制成交量柱
    klineData.forEach((candle: KLineData, index: number) => {
      const x = index * candleWidth + candleWidth / 2 - panOffset;
      
      // 只绘制可见区域的成交量柱
      if (x < -candleWidth || x > width - 80) return;
      
      const barHeight = (candle.volume / maxVolume) * (height - 20);
      const barTop = height - barHeight - 10;
      
      const isGreen = candle.close >= candle.open;
      ctx.fillStyle = isGreen ? '#00d4aa' : '#f84960';
      ctx.fillRect(x - candleWidth / 2 + 1, barTop, candleWidth - 2, barHeight);
    });
  }, [klineData, zoomLevel, panOffset]);


  // 时间格式化
  const formatTime = (timestamp: number) => {
    const date = new Date(timestamp);
    return date.toLocaleTimeString('zh-CN', { 
      hour: '2-digit', 
      minute: '2-digit',
      hour12: false 
    });
  };

  // 计算统计数据
  const stats = React.useMemo(() => {
    if (!klineData || klineData.length === 0) return null;
    
    const latest = klineData[klineData.length - 1];
    const previous = klineData[klineData.length - 2];
    const change = previous ? latest.close - previous.close : 0;
    const changePercent = previous ? (change / previous.close) * 100 : 0;
    
    const high24h = Math.max(...klineData.map((k: KLineData) => k.high));
    const low24h = Math.min(...klineData.map((k: KLineData) => k.low));
    const volume24h = klineData.reduce((sum: number, k: KLineData) => sum + k.volume, 0);
    
    return {
      price: latest.close,
      change,
      changePercent,
      high24h,
      low24h,
      volume24h
    };
  }, [klineData]);

  // 计算移动平均线值
  const maValues = React.useMemo(() => {
    if (!klineData || klineData.length === 0) return { ma5: 0, ma10: 0, ma30: 0 };
    
    const latest = klineData[klineData.length - 1];
    const ma5Values = calculateMA(klineData, 5);
    const ma10Values = calculateMA(klineData, 10);
    const ma30Values = calculateMA(klineData, 30);
    
    return {
      ma5: ma5Values[ma5Values.length - 1] || latest.close,
      ma10: ma10Values[ma10Values.length - 1] || latest.close,
      ma30: ma30Values[ma30Values.length - 1] || latest.close
    };
  }, [klineData, calculateMA]);

  // 绘制图表
  useEffect(() => {
    drawPriceChart();
    drawVolumeChart();
  }, [drawPriceChart, drawVolumeChart]);

  // 窗口大小变化时重新绘制
  useEffect(() => {
    const handleResize = () => {
      drawPriceChart();
      drawVolumeChart();
    };

    window.addEventListener('resize', handleResize);
    return () => window.removeEventListener('resize', handleResize);
  }, [drawPriceChart, drawVolumeChart]);

  return (
    <Container>
      <ChartHeader>
        <SymbolInfo>
          {stats && (
            <>
              <PriceDisplay isPositive={stats.changePercent >= 0}>
                {stats.price.toFixed(2)}
              </PriceDisplay>
              <ChangeDisplay isPositive={stats.changePercent >= 0}>
                <div>{stats.change >= 0 ? '+' : ''}{stats.change.toFixed(2)}</div>
                <div>({stats.changePercent >= 0 ? '+' : ''}{stats.changePercent.toFixed(2)}%)</div>
              </ChangeDisplay>
            </>
          )}
        </SymbolInfo>
        
        <div style={{ display: 'flex', alignItems: 'center', gap: '16px' }}>
          {isConnected && <span style={{ color: '#00d4aa', fontSize: '12px' }}>●实时</span>}
          {lastUpdate > 0 && (
            <span style={{ color: '#848e9c', fontSize: '12px' }}>
              {formatTime(lastUpdate)}
            </span>
          )}
          <div style={{ display: 'flex', alignItems: 'center', gap: '4px' }}>
            <span style={{ color: '#848e9c', fontSize: '12px' }}>
              缩放: {zoomLevel.toFixed(1)}x
            </span>
            <button
              onClick={() => {
                setZoomLevel(1);
                setPanOffset(0);
              }}
              style={{
                padding: '2px 6px',
                background: 'transparent',
                border: '1px solid #2b3139',
                color: '#848e9c',
                borderRadius: '3px',
                fontSize: '10px',
                cursor: 'pointer'
              }}
              title="重置缩放"
            >
              重置
            </button>
          </div>
          <TimeframeSelector>
            {timeframes.map(tf => (
              <TimeframeButton
                key={tf}
                active={timeframe === tf}
                onClick={() => onTimeframeChange(tf)}
              >
                {tf}
              </TimeframeButton>
            ))}
          </TimeframeSelector>
        </div>
      </ChartHeader>
      
      <ChartContainer 
        ref={containerRef} 
        onMouseMove={handleMouseMove} 
        onMouseDown={handleMouseDown}
        onMouseUp={handleMouseUp}
        onMouseLeave={handleMouseLeave}
        onWheel={handleWheel}
        style={{ cursor: isDragging ? 'grabbing' : 'grab' }}
      >
        <PriceChart>
          <MovingAverages>
            <MAItem color="#f0b90b">MA5: {maValues.ma5.toFixed(2)}</MAItem>
            <MAItem color="#ff6b35">MA10: {maValues.ma10.toFixed(2)}</MAItem>
            <MAItem color="#8b5cf6">MA30: {maValues.ma30.toFixed(2)}</MAItem>
          </MovingAverages>
          
          <Canvas ref={priceCanvasRef} />
          <PriceAxis>
            {stats && (
              <>
                <div>{stats.high24h.toFixed(2)}</div>
                <div>{stats.price.toFixed(2)}</div>
                <div>{stats.low24h.toFixed(2)}</div>
              </>
            )}
          </PriceAxis>
          <TimeAxis>
            {klineData && klineData.length > 0 && (
              <>
                <div>{formatTime(klineData[0].timestamp)}</div>
                <div>{formatTime(klineData[Math.floor(klineData.length / 2)].timestamp)}</div>
                <div>{formatTime(klineData[klineData.length - 1].timestamp)}</div>
              </>
            )}
          </TimeAxis>
        </PriceChart>
        
        <VolumeChart>
          <Canvas ref={volumeCanvasRef} />
        </VolumeChart>
        
        {klineLoading && (
          <LoadingOverlay>
            <div>正在加载K线数据...</div>
          </LoadingOverlay>
        )}
        
        {klineError && (
          <LoadingOverlay>
            <div style={{ textAlign: 'center' }}>
              <div>暂无K线数据</div>
            </div>
          </LoadingOverlay>
        )}
        
        <Tooltip visible={tooltip.visible} x={tooltip.x} y={tooltip.y}>
          {tooltip.data && (
            <>
              <TooltipRow>
                <TooltipLabel>时间:</TooltipLabel>
                <TooltipValue>{formatTime(tooltip.data.timestamp)}</TooltipValue>
              </TooltipRow>
              <TooltipRow>
                <TooltipLabel>开盘:</TooltipLabel>
                <TooltipValue>{tooltip.data.open.toFixed(2)}</TooltipValue>
              </TooltipRow>
              <TooltipRow>
                <TooltipLabel>最高:</TooltipLabel>
                <TooltipValue style={{ color: '#00d4aa' }}>{tooltip.data.high.toFixed(2)}</TooltipValue>
              </TooltipRow>
              <TooltipRow>
                <TooltipLabel>最低:</TooltipLabel>
                <TooltipValue style={{ color: '#f84960' }}>{tooltip.data.low.toFixed(2)}</TooltipValue>
              </TooltipRow>
              <TooltipRow>
                <TooltipLabel>收盘:</TooltipLabel>
                <TooltipValue>{tooltip.data.close.toFixed(2)}</TooltipValue>
              </TooltipRow>
              <TooltipRow>
                <TooltipLabel>成交量:</TooltipLabel>
                <TooltipValue>{tooltip.data.volume.toFixed(4)}</TooltipValue>
              </TooltipRow>
            </>
          )}
        </Tooltip>
      </ChartContainer>
    </Container>
  );
};
