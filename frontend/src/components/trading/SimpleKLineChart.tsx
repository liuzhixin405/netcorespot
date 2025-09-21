import React, { useState, useEffect, useCallback } from 'react';
import styled from 'styled-components';
import { useTradingPair } from '../../hooks/useTrading';
import { useKLineWithRealTime } from '../../hooks/useKLineWithRealTime';

const Container = styled.div`
  height: 100%;
  background: #161b22;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  border: 1px solid #30363d;
`;

const ChartHeader = styled.div`
  padding: 0.5rem 1rem;
  border-bottom: 1px solid #30363d;
  background: #21262d;
  font-size: 0.8rem;
  font-weight: 600;
  color: #f0f6fc;
  display: flex;
  align-items: center;
  justify-content: space-between;
`;

const TimeframeSelector = styled.div`
  display: flex;
  gap: 0.25rem;
`;

const TimeframeButton = styled.button<{ active: boolean }>`
  padding: 0.25rem 0.5rem;
  border: 1px solid ${props => props.active ? '#f0f6fc' : '#30363d'};
  background: ${props => props.active ? '#f0f6fc' : 'transparent'};
  color: ${props => props.active ? '#0d1117' : '#7d8590'};
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s;
  
  &:hover {
    border-color: #f0f6fc;
    color: #f0f6fc;
  }
`;


const ChartContent = styled.div`
  flex: 1;
  display: flex;
  flex-direction: column;
  padding: 1rem;
  gap: 1rem;
`;

const PriceChart = styled.div`
  flex: 1;
  position: relative;
  background: #0d1117;
  border: 1px solid #30363d;
  border-radius: 6px;
  overflow: hidden;
`;

const VolumeChart = styled.div`
  height: 80px;
  position: relative;
  background: #0d1117;
  border: 1px solid #30363d;
  border-radius: 6px;
  overflow: hidden;
  
  /* 添加水平网格线 */
  &::before {
    content: '';
    position: absolute;
    top: 25%;
    left: 0;
    right: 0;
    height: 1px;
    background: rgba(255, 255, 255, 0.05);
    z-index: 1;
  }
  
  &::after {
    content: '';
    position: absolute;
    top: 50%;
    left: 0;
    right: 0;
    height: 1px;
    background: rgba(255, 255, 255, 0.08);
    z-index: 1;
  }
`;

const ChartGrid = styled.div`
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  pointer-events: none;

  /* 水平网格线 */
  &::before {
    content: '';
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
    background-image: linear-gradient(to bottom, 
      transparent 0%, 
      transparent 19%, 
      rgba(255, 255, 255, 0.08) 20%, 
      rgba(255, 255, 255, 0.08) 20%, 
      transparent 21%,
      transparent 39%, 
      rgba(255, 255, 255, 0.12) 40%, 
      rgba(255, 255, 255, 0.12) 40%, 
      transparent 41%,
      transparent 59%, 
      rgba(255, 255, 255, 0.08) 60%, 
      rgba(255, 255, 255, 0.08) 60%, 
      transparent 61%,
      transparent 79%, 
      rgba(255, 255, 255, 0.08) 80%, 
      rgba(255, 255, 255, 0.08) 80%, 
      transparent 81%
    );
  }
  
  /* 垂直网格线 */
  &::after {
    content: '';
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    bottom: 0;
    background-image: linear-gradient(to right, 
      rgba(255, 255, 255, 0.05) 1px, 
      transparent 1px
    );
    background-size: 8% 100%;
  }
`;

const CandlestickContainer = styled.div`
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  display: flex;
  align-items: end;
  padding: 10px 2px;
  z-index: 2;
`;

const CandlestickItem = styled.div<{ 
  candleHeight: number; 
  candleTop: number;
  wickTop: number;
  wickBottom: number;
  isGreen: boolean;
  dataCount: number;
}>`
  flex: 1;
  max-width: ${props => props.dataCount > 100 ? '4px' : props.dataCount > 50 ? '6px' : '8px'};
  min-width: 2px;
  height: 100%;
  position: relative;
  display: flex;
  justify-content: center;
  cursor: crosshair;
  
  /* 上下影线 */
  &::before {
    content: '';
    position: absolute;
    left: 50%;
    transform: translateX(-50%);
    width: 1px;
    top: ${props => props.wickTop}%;
    bottom: ${props => props.wickBottom}%;
    background: ${props => props.isGreen ? '#00b35f' : '#e84142'};
    z-index: 1;
  }
  
  /* K线实体 */
  &::after {
    content: '';
    position: absolute;
    left: 50%;
    transform: translateX(-50%);
    width: ${props => props.dataCount > 100 ? '3px' : props.dataCount > 50 ? '5px' : '7px'};
    height: ${props => Math.max(props.candleHeight, 1)}%;
    bottom: ${props => props.candleTop}%;
    background: ${props => props.isGreen ? '#0d1117' : '#e84142'};
    border: 1px solid ${props => props.isGreen ? '#00b35f' : '#e84142'};
    z-index: 2;
  }
  
  &:hover {
    &::before {
      background: ${props => props.isGreen ? '#00d66a' : '#ff4d4f'};
    }
    
    &::after {
      border-color: ${props => props.isGreen ? '#00d66a' : '#ff4d4f'};
      box-shadow: 0 0 3px ${props => props.isGreen ? '#00b35f' : '#e84142'};
    }
  }
`;

const VolumeBars = styled.div`
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  display: flex;
  align-items: end;
  padding: 0 2px;
  z-index: 2;
`;

const VolumeBar = styled.div<{ 
  height: number; 
  isGreen: boolean; 
  dataCount: number;
  isSelected: boolean;
}>`
  flex: 1;
  max-width: ${props => props.dataCount > 100 ? '4px' : props.dataCount > 50 ? '6px' : '8px'};
  min-width: 1px;
  height: ${props => props.height}%;
  background: ${props => props.isGreen ? '#00b35f' : '#e84142'};
  opacity: ${props => props.isSelected ? 1 : 0.6};
  min-height: 2px;
  transition: all 0.2s ease;
  position: relative;
  cursor: pointer;
  
  &:hover {
    opacity: 0.9;
    transform: scaleY(1.02);
  }
  
  ${props => props.isSelected && `
    box-shadow: 0 0 4px ${props.isGreen ? '#00b35f' : '#e84142'};
    border-top: 2px solid ${props.isGreen ? '#00d66a' : '#ff4d4f'};
  `}
`;

const PriceLabel = styled.div`
  position: absolute;
  top: 0.5rem;
  right: 0.5rem;
  background: rgba(13, 17, 23, 0.8);
  padding: 0.25rem 0.5rem;
  border-radius: 4px;
  font-size: 0.8rem;
  font-weight: 600;
  color: #f0f6fc;
  border: 1px solid #30363d;
`;

const CurrentPrice = styled.div<{ isPositive: boolean }>`
  color: ${props => props.isPositive ? '#3fb950' : '#f85149'};
`;

const TimeAxis = styled.div`
  height: 35px;
  background: #161b22;
  border-top: 1px solid #30363d;
  display: flex;
  align-items: center;
  padding: 0 1rem;
  position: relative;
  overflow: hidden;
`;

const TimeLabel = styled.div<{ position: number }>`
  position: absolute;
  left: ${props => props.position}%;
  transform: translateX(-50%);
  font-size: 0.65rem;
  color: #7d8590;
  white-space: nowrap;
  max-width: 80px;
  text-align: center;
  line-height: 1.2;
  
  /* 防止标签重叠 */
  &:first-child {
    transform: translateX(0);
    left: ${props => Math.max(props.position, 5)}%;
  }
  
  &:last-child {
    transform: translateX(-100%);
    left: ${props => Math.min(props.position, 95)}%;
  }
`;

const ChartTooltip = styled.div<{ x: number; y: number; visible: boolean }>`
  position: absolute;
  left: ${props => props.x}px;
  top: ${props => props.y}px;
  background: rgba(13, 17, 23, 0.95);
  border: 1px solid #30363d;
  border-radius: 6px;
  padding: 0.75rem;
  font-size: 0.75rem;
  color: #f0f6fc;
  pointer-events: none;
  z-index: 1000;
  display: ${props => props.visible ? 'block' : 'none'};
  min-width: 180px;
  backdrop-filter: blur(10px);
`;

const TooltipRow = styled.div`
  display: flex;
  justify-content: space-between;
  margin-bottom: 0.25rem;
  
  &:last-child {
    margin-bottom: 0;
  }
`;

const TooltipLabel = styled.span`
  color: #7d8590;
  margin-right: 1rem;
`;

const TooltipValue = styled.span<{ color?: string }>`
  color: ${props => props.color || '#f0f6fc'};
  font-weight: 600;
`;

const ChartOverlay = styled.div`
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  cursor: crosshair;
`;

const TrendLine = styled.svg`
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  pointer-events: none;
  z-index: 3;
`;

const TrendPath = styled.path<{ color: string }>`
  fill: none;
  stroke: ${props => props.color};
  stroke-width: 1.5;
  stroke-opacity: 0.8;
  filter: drop-shadow(0 0 2px ${props => props.color}40);
`;

const MALine = styled.path<{ color: string }>`
  fill: none;
  stroke: ${props => props.color};
  stroke-width: 1.2;
  stroke-opacity: 0.9;
  stroke-dasharray: none;
`;

const MALegend = styled.div`
  position: absolute;
  top: 0.5rem;
  left: 0.5rem;
  display: flex;
  gap: 1rem;
  font-size: 0.7rem;
  z-index: 4;
`;

const MAItem = styled.div<{ color: string }>`
  color: ${props => props.color};
  font-weight: 500;
`;

const VolumeInfo = styled.div<{ x: number; y: number; visible: boolean }>`
  position: absolute;
  left: ${props => props.x}px;
  bottom: ${props => props.y}px;
  background: rgba(13, 17, 23, 0.95);
  border: 1px solid #30363d;
  border-radius: 6px;
  padding: 0.5rem;
  font-size: 0.7rem;
  color: #f0f6fc;
  pointer-events: none;
  z-index: 1000;
  display: ${props => props.visible ? 'block' : 'none'};
  min-width: 120px;
  backdrop-filter: blur(10px);
`;

const VolumeInfoRow = styled.div`
  display: flex;
  justify-content: space-between;
  margin-bottom: 0.25rem;
  
  &:last-child {
    margin-bottom: 0;
  }
`;

const VolumeInfoLabel = styled.span`
  color: #7d8590;
  margin-right: 0.5rem;
`;

const VolumeInfoValue = styled.span<{ color?: string }>`
  color: ${props => props.color || '#f0f6fc'};
  font-weight: 600;
`;

interface SimpleKLineChartProps {
  symbol: string;
  timeframe: string;
  onTimeframeChange: (timeframe: string) => void;
}

const SimpleKLineChart: React.FC<SimpleKLineChartProps> = ({ symbol, timeframe, onTimeframeChange }) => {
  const timeframes = ['1m', '5m', '15m', '1h', '4h', '1d'];
  
  
  const [currentPrice, setCurrentPrice] = useState(0);
  const [priceChange, setPriceChange] = useState(0);
  const [tooltip, setTooltip] = useState({
    visible: false,
    x: 0,
    y: 0,
    data: null as any
  });
  
  const [zoomLevel, setZoomLevel] = useState(1);
  const [scrollOffset, setScrollOffset] = useState(0);
  const [selectedVolumeIndex, setSelectedVolumeIndex] = useState<number | null>(null);
  
  // 使用API获取历史数据，SignalR获取实时更新
  const dataLimit = Math.max(Math.min(Math.floor(100 / zoomLevel), 200), 50); // 最少50条数据以支持MA30
  const { 
    data: klineData, 
    loading: klineLoading, 
    error: klineError, 
    isConnected,
    lastUpdate,
    refresh,
    reconnect
  } = useKLineWithRealTime({
    symbol,
    timeframe,
    limit: dataLimit
  });
  const { pair, loading: pairLoading } = useTradingPair(symbol);

  // 更新当前价格和变化
  useEffect(() => {
    if (pair) {
      setCurrentPrice(pair.price);
      setPriceChange(pair.change24h);
    }
  }, [pair]);

  // 时间段切换时重置所有状态
  useEffect(() => {
    // 重置tooltip和选中状态
    setTooltip({ visible: false, x: 0, y: 0, data: null });
    setSelectedVolumeIndex(null);
    
    // 重置滚动和缩放
    setScrollOffset(0);
  }, [symbol, timeframe]);

  // 时间格式化函数
  const formatTime = useCallback((timestamp: number, timeframeParam: string, klineDataArray?: any[]): string => {
    const date = new Date(timestamp);
    const now = new Date();
    const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    const dataDate = new Date(date.getFullYear(), date.getMonth(), date.getDate());
    
    // 检查是否跨天 - 如果数据跨越多天，短时间框架也需要显示日期
    const isMultipleDays = klineDataArray && klineDataArray.length > 0 && (() => {
      const timestamps = klineDataArray.map(k => k.timestamp);
      const minDate = new Date(Math.min(...timestamps));
      const maxDate = new Date(Math.max(...timestamps));
      const minDay = new Date(minDate.getFullYear(), minDate.getMonth(), minDate.getDate());
      const maxDay = new Date(maxDate.getFullYear(), maxDate.getMonth(), maxDate.getDate());
      return maxDay.getTime() - minDay.getTime() > 0;
    })();
    
    switch (timeframeParam) {
      case '1m':
      case '5m':
      case '15m':
        // 如果数据跨天或不是今天，显示日期+时间
        if (isMultipleDays || dataDate.getTime() !== today.getTime()) {
          return date.toLocaleDateString('zh-CN', { 
            month: 'numeric',
            day: 'numeric',
            hour: '2-digit', 
            minute: '2-digit',
            hour12: false 
          });
        } else {
          // 同一天的数据只显示时间
          return date.toLocaleTimeString('zh-CN', { 
            hour: '2-digit', 
            minute: '2-digit',
            hour12: false 
          });
        }
      case '1h':
      case '4h':
        return date.toLocaleDateString('zh-CN', { 
          month: 'short', 
          day: 'numeric',
          hour: '2-digit',
          hour12: false
        });
      case '1d':
        return date.toLocaleDateString('zh-CN', { 
          month: 'short', 
          day: 'numeric' 
        });
      default:
        return date.toLocaleString('zh-CN');
    }
  }, []);

  // 计算移动平均线
  const calculateMA = useCallback((data: any[], period: number): number[] => {
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
    const delta = event.deltaY > 0 ? 0.9 : 1.1;
    const newZoom = Math.max(0.5, Math.min(zoomLevel * delta, 5));
    setZoomLevel(newZoom);
  }, [zoomLevel]);

  // 鼠标悬停处理
  const handleMouseMove = (event: React.MouseEvent<HTMLDivElement>) => {
    if (!candlestickData || candlestickData.length === 0) return;

    const rect = event.currentTarget.getBoundingClientRect();
    const x = event.clientX - rect.left;
    const y = event.clientY - rect.top;
    
    // 计算悬停位置对应的数据点
    const chartWidth = rect.width;
    const dataIndex = Math.floor((x / chartWidth) * candlestickData.length);
    
    if (dataIndex >= 0 && dataIndex < candlestickData.length) {
      const candleData = candlestickData[dataIndex];
      setTooltip({
        visible: true,
        x: Math.min(x + 10, chartWidth - 200), // 防止tooltip超出边界
        y: Math.max(y - 120, 10),
        data: candleData.data
      });
    }
  };

  const handleMouseLeave = () => {
    setTooltip(prev => ({ ...prev, visible: false }));
  };

  // 成交量柱点击处理
  const handleVolumeBarClick = useCallback((index: number, event: React.MouseEvent) => {
    event.stopPropagation();
    setSelectedVolumeIndex(prev => prev === index ? null : index);
  }, []);

  // 成交量柱悬停处理
  const handleVolumeBarHover = useCallback((index: number, event: React.MouseEvent) => {
    if (!klineData || !klineData[index]) return;

    const rect = event.currentTarget.getBoundingClientRect();
    const volumeChart = event.currentTarget.closest('.volume-chart') as HTMLElement;
    const chartRect = volumeChart?.getBoundingClientRect();
    
    if (chartRect) {
      const x = rect.left - chartRect.left + rect.width / 2;
      const y = chartRect.height - rect.bottom + chartRect.top + 10;
      
      setTooltip({
        visible: true,
        x: Math.min(x, chartRect.width - 150),
        y: y,
        data: klineData[index]
      });
    }
  }, [klineData]);

  const handleVolumeMouseLeave = useCallback(() => {
    setTooltip(prev => ({ ...prev, visible: false }));
  }, []);

  // 生成蜡烛图数据
  const candlestickData = React.useMemo(() => {
    if (!klineData || klineData.length === 0) {
      return [];
    }

    // 计算价格范围
    const allPrices = klineData.flatMap(k => [k.open, k.high, k.low, k.close]);
    const minPrice = Math.min(...allPrices);
    const maxPrice = Math.max(...allPrices);
    const priceRange = maxPrice - minPrice || 1;
    
    return klineData.map((k, i) => {
      const isGreen = k.close >= k.open;
      
      // 计算各个价格点的垂直位置（百分比，从底部算起）
      const highPos = ((k.high - minPrice) / priceRange) * 80 + 10; // 10-90% 范围
      const lowPos = ((k.low - minPrice) / priceRange) * 80 + 10;
      const openPos = ((k.open - minPrice) / priceRange) * 80 + 10;
      const closePos = ((k.close - minPrice) / priceRange) * 80 + 10;
      
      // 实体的顶部和高度
      const candleTop = Math.min(openPos, closePos);
      const candleHeight = Math.abs(closePos - openPos);
      
      // 影线的顶部和底部
      const wickTop = 100 - highPos; // 从顶部算起
      const wickBottom = 100 - lowPos; // 从顶部算起
      
      return {
        candleHeight: candleHeight,
        candleTop: candleTop,
        wickTop: wickTop,
        wickBottom: wickBottom,
        isGreen: isGreen,
        data: k
      };
    });
  }, [klineData]);

  // 生成成交量数据 - 只使用真实数据
  const volumeData = React.useMemo(() => {
    if (!klineData || klineData.length === 0) {
      // 无数据时返回空数组，不显示任何模拟数据
      return [];
    }

    // 使用真实成交量数据
    const volumes = klineData.map(k => k.volume);
    const maxVolume = Math.max(...volumes);
    
    return klineData.map(k => ({
      height: maxVolume > 0 ? (k.volume / maxVolume) * 80 + 10 : 10,
      isGreen: k.close >= k.open
    }));
  }, [klineData]);

  // 生成时间轴标签
  const timeLabels = React.useMemo(() => {
    if (!klineData || klineData.length === 0) return [];

    const labels = [];
    const totalPoints = klineData.length;
    
    // 根据时间框架调整标签数量和策略
    let labelCount = 6;
    if (timeframe === '1m') labelCount = 8; // 1分钟图显示更多标签
    else if (timeframe === '5m') labelCount = 7;
    else if (timeframe === '15m') labelCount = 6;
    
    labelCount = Math.min(labelCount, totalPoints);
    
    // 检查数据是否跨天
    const timestamps = klineData.map(k => k.timestamp);
    const minDate = new Date(Math.min(...timestamps));
    const maxDate = new Date(Math.max(...timestamps));
    const isMultipleDays = new Date(maxDate.toDateString()).getTime() !== new Date(minDate.toDateString()).getTime();
    
    for (let i = 0; i < labelCount; i++) {
      const index = Math.floor((i / (labelCount - 1)) * (totalPoints - 1));
      const data = klineData[index];
      if (data) {
        let timeText = formatTime(data.timestamp, timeframe, klineData);
        
        // 对于短时间框架，如果跨天，在第一个和最后一个标签显示完整日期
        if ((timeframe === '1m' || timeframe === '5m' || timeframe === '15m') && isMultipleDays) {
          if (i === 0 || i === labelCount - 1) {
            const date = new Date(data.timestamp);
            timeText = date.toLocaleDateString('zh-CN', { 
              month: 'numeric',
              day: 'numeric',
              hour: '2-digit', 
              minute: '2-digit',
              hour12: false 
            });
          } else {
            // 中间的标签只显示时间
            const date = new Date(data.timestamp);
            timeText = date.toLocaleTimeString('zh-CN', { 
              hour: '2-digit', 
              minute: '2-digit',
              hour12: false 
            });
          }
        }
        
        labels.push({
          position: (index / (totalPoints - 1)) * 100,
          time: timeText
        });
      }
    }
    
    return labels;
  }, [klineData, timeframe, formatTime]);

  // 生成趋势线数据
  const trendLineData = React.useMemo(() => {
    if (!klineData || klineData.length < 2) {
      return { path: '', color: '#7d8590' };
    }

    // 计算价格范围
    const allPrices = klineData.flatMap(k => [k.open, k.high, k.low, k.close]);
    const minPrice = Math.min(...allPrices);
    const maxPrice = Math.max(...allPrices);
    const priceRange = maxPrice - minPrice || 1;
    
    // 生成收盘价连线的路径
    const pathPoints = klineData.map((k, i) => {
      const x = (i / (klineData.length - 1)) * 100;
      const y = 100 - (((k.close - minPrice) / priceRange) * 80 + 10); // 10-90% 范围，从顶部算起
      return `${x},${y}`;
    });
    
    const path = `M ${pathPoints.join(' L ')}`;
    
    // 判断整体趋势颜色
    const firstPrice = klineData[0].close;
    const lastPrice = klineData[klineData.length - 1].close;
    const color = lastPrice >= firstPrice ? '#00b35f' : '#e84142';
    
    return { path, color };
  }, [klineData]);

  // 生成移动平均线数据
  const movingAverages = React.useMemo(() => {
    if (!klineData || klineData.length < 5) {
      return { ma5: '', ma10: '', ma30: '' };
    }

    // 计算价格范围
    const allPrices = klineData.flatMap(k => [k.open, k.high, k.low, k.close]);
    const minPrice = Math.min(...allPrices);
    const maxPrice = Math.max(...allPrices);
    const priceRange = maxPrice - minPrice || 1;

    // 计算各个周期的移动平均线
    const ma5Values = calculateMA(klineData, 5);
    const ma10Values = calculateMA(klineData, 10);
    const ma30Values = calculateMA(klineData, 30);

    // 生成SVG路径
    const createMAPath = (values: number[], startIndex: number) => {
      if (values.length === 0) return '';
      
      const points = values.map((value, i) => {
        const dataIndex = startIndex + i;
        const x = (dataIndex / (klineData.length - 1)) * 100;
        const y = 100 - (((value - minPrice) / priceRange) * 80 + 10);
    return `${x},${y}`;
      });
      
      return `M ${points.join(' L ')}`;
    };

    return {
      ma5: klineData.length >= 5 ? createMAPath(ma5Values, 4) : '',   // MA5从第5个数据点开始
      ma10: klineData.length >= 10 ? createMAPath(ma10Values, 9) : '', // MA10从第10个数据点开始
      ma30: klineData.length >= 30 ? createMAPath(ma30Values, 29) : '' // MA30从第30个数据点开始
    };
  }, [klineData, calculateMA]);

  return (
    <Container>
      <ChartHeader>
        <div>
          {symbol} - {timeframe} (数据:{klineData?.length || 0}条, 缩放:{zoomLevel.toFixed(1)}x)
          {isConnected && <span style={{ marginLeft: '8px', color: '#00b35f' }}>●SignalR实时推送</span>}
          {!isConnected && !klineLoading && <span style={{ marginLeft: '8px', color: '#f85149' }}>●离线</span>}
          {klineLoading && <span style={{ marginLeft: '8px', color: '#7d8590' }}>加载中...</span>}
          {klineError && (
            <span style={{ marginLeft: '8px', color: '#f85149', cursor: 'pointer' }} onClick={reconnect}>
              连接失败(点击重连)
            </span>
          )}
          {lastUpdate > 0 && (
            <span style={{ marginLeft: '8px', color: '#7d8590', fontSize: '0.6rem' }}>
              {new Date(lastUpdate).toLocaleTimeString()}
            </span>
          )}
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
      </ChartHeader>
      
      <ChartContent>
        <PriceChart>
          <ChartGrid />
          {klineData && klineData.length > 0 ? (
            <CandlestickContainer>
              {candlestickData.map((candle, index) => (
                <CandlestickItem
                  key={index}
                  candleHeight={candle.candleHeight}
                  candleTop={candle.candleTop}
                  wickTop={candle.wickTop}
                  wickBottom={candle.wickBottom}
                  isGreen={candle.isGreen}
                  dataCount={candlestickData.length}
                />
              ))}
            </CandlestickContainer>
          ) : (
            /* 无数据状态 */
            <div style={{
              position: 'absolute',
              top: '50%',
              left: '50%',
              transform: 'translate(-50%, -50%)',
              textAlign: 'center',
              color: '#7d8590',
              fontSize: '0.9rem',
              zIndex: 10
            }}>
              {klineError ? (
                <div>
                  <div style={{ marginBottom: '8px' }}>K线数据连接失败</div>
                  <div style={{ fontSize: '0.7rem', opacity: 0.7, marginBottom: '8px' }}>
                    需要启动后端gRPC服务
                  </div>
                  <div 
                    style={{ fontSize: '0.7rem', color: '#58a6ff', cursor: 'pointer' }}
                    onClick={reconnect}
                  >
                    点击重连
                  </div>
                </div>
              ) : klineLoading ? (
                <div>
                  <div style={{ marginBottom: '8px' }}>正在连接K线服务...</div>
                  <div style={{ fontSize: '0.7rem', opacity: 0.7 }}>
                    等待后端服务响应
                  </div>
                </div>
              ) : (
                <div>
                  <div style={{ marginBottom: '8px' }}>K线数据暂无</div>
                  <div style={{ fontSize: '0.7rem', opacity: 0.7 }}>
                    等待后端数据推送
                  </div>
                </div>
              )}
            </div>
          )}
          
          {/* 移动平均线 */}
          <TrendLine viewBox="0 0 100 100" preserveAspectRatio="none">
            {movingAverages.ma5 && <MALine d={movingAverages.ma5} color="#ffa500" />}
            {movingAverages.ma10 && <MALine d={movingAverages.ma10} color="#00bfff" />}
            {movingAverages.ma30 && <MALine d={movingAverages.ma30} color="#9966cc" />}
          </TrendLine>
          
          {/* MA图例 */}
          <MALegend>
            {klineData && klineData.length >= 5 && (
              <MAItem color="#ffa500">
                MA5 {calculateMA(klineData, 5).slice(-1)[0]?.toFixed(2)}
              </MAItem>
            )}
            {klineData && klineData.length >= 10 && (
              <MAItem color="#00bfff">
                MA10 {calculateMA(klineData, 10).slice(-1)[0]?.toFixed(2)}
              </MAItem>
            )}
            {klineData && klineData.length >= 30 && (
              <MAItem color="#9966cc">
                MA30 {calculateMA(klineData, 30).slice(-1)[0]?.toFixed(2)}
              </MAItem>
            )}
          </MALegend>
          
          <ChartOverlay 
            onMouseMove={handleMouseMove}
            onMouseLeave={handleMouseLeave}
            onWheel={handleWheel}
            onClick={() => setSelectedVolumeIndex(null)}
          />
          <PriceLabel>
            <div style={{ marginBottom: '4px' }}>
            <CurrentPrice isPositive={priceChange >= 0}>
              {currentPrice.toLocaleString()}
            </CurrentPrice>
              <div style={{ color: priceChange >= 0 ? '#3fb950' : '#f85149', fontSize: '0.7rem' }}>
              {priceChange >= 0 ? '+' : ''}{priceChange.toFixed(2)}%
            </div>
            </div>
            {klineData && klineData.length > 0 && (
              <div style={{ fontSize: '0.6rem', color: '#7d8590', lineHeight: '1.2' }}>
                <div>O: {klineData[klineData.length - 1]?.open?.toLocaleString()}</div>
                <div>H: {klineData[klineData.length - 1]?.high?.toLocaleString()}</div>
                <div>L: {klineData[klineData.length - 1]?.low?.toLocaleString()}</div>
                <div>V: {klineData[klineData.length - 1]?.volume?.toFixed(2)}</div>
              </div>
            )}
          </PriceLabel>
          
          {/* Tooltip */}
          <ChartTooltip 
            x={tooltip.x} 
            y={tooltip.y} 
            visible={tooltip.visible && tooltip.data}
          >
            {tooltip.data && (
              <>
                <TooltipRow>
                  <TooltipLabel>时间:</TooltipLabel>
                  <TooltipValue>
                    {new Date(tooltip.data.timestamp).toLocaleString('zh-CN')}
                  </TooltipValue>
                </TooltipRow>
                <TooltipRow>
                  <TooltipLabel>开盘:</TooltipLabel>
                  <TooltipValue>{tooltip.data.open.toLocaleString()}</TooltipValue>
                </TooltipRow>
                <TooltipRow>
                  <TooltipLabel>最高:</TooltipLabel>
                  <TooltipValue color="#3fb950">{tooltip.data.high.toLocaleString()}</TooltipValue>
                </TooltipRow>
                <TooltipRow>
                  <TooltipLabel>最低:</TooltipLabel>
                  <TooltipValue color="#f85149">{tooltip.data.low.toLocaleString()}</TooltipValue>
                </TooltipRow>
                <TooltipRow>
                  <TooltipLabel>收盘:</TooltipLabel>
                  <TooltipValue 
                    color={tooltip.data.close >= tooltip.data.open ? '#3fb950' : '#f85149'}
                  >
                    {tooltip.data.close.toLocaleString()}
                  </TooltipValue>
                </TooltipRow>
                <TooltipRow>
                  <TooltipLabel>成交量:</TooltipLabel>
                  <TooltipValue>{tooltip.data.volume.toFixed(2)}</TooltipValue>
                </TooltipRow>
              </>
            )}
          </ChartTooltip>
        </PriceChart>
        
        <VolumeChart className="volume-chart">
          {volumeData.length > 0 ? (
          <VolumeBars>
            {volumeData.map((bar, index) => (
              <VolumeBar
                key={index}
                height={bar.height}
                isGreen={bar.isGreen}
                  dataCount={volumeData.length}
                  isSelected={selectedVolumeIndex === index}
                  onClick={(e) => handleVolumeBarClick(index, e)}
                  onMouseEnter={(e) => handleVolumeBarHover(index, e)}
                  onMouseLeave={handleVolumeMouseLeave}
              />
            ))}
          </VolumeBars>
          ) : (
            /* 成交量无数据状态 */
            <div style={{
              position: 'absolute',
              top: '50%',
              left: '50%',
              transform: 'translate(-50%, -50%)',
              textAlign: 'center',
              color: '#7d8590',
              fontSize: '0.8rem',
              opacity: 0.7
            }}>
              成交量暂无数据
            </div>
          )}
          
          {/* 成交量信息提示 */}
          <VolumeInfo 
            x={tooltip.x} 
            y={120} 
            visible={tooltip.visible && tooltip.data}
          >
            {tooltip.data && (
              <>
                <VolumeInfoRow>
                  <VolumeInfoLabel>时间:</VolumeInfoLabel>
                  <VolumeInfoValue>
                    {formatTime(tooltip.data.timestamp, timeframe, klineData)}
                  </VolumeInfoValue>
                </VolumeInfoRow>
                <VolumeInfoRow>
                  <VolumeInfoLabel>成交量:</VolumeInfoLabel>
                  <VolumeInfoValue color={tooltip.data.close >= tooltip.data.open ? '#00b35f' : '#e84142'}>
                    {tooltip.data.volume.toLocaleString()}
                  </VolumeInfoValue>
                </VolumeInfoRow>
                <VolumeInfoRow>
                  <VolumeInfoLabel>成交额:</VolumeInfoLabel>
                  <VolumeInfoValue>
                    {(tooltip.data.volume * tooltip.data.close).toLocaleString()}
                  </VolumeInfoValue>
                </VolumeInfoRow>
                <VolumeInfoRow>
                  <VolumeInfoLabel>价格:</VolumeInfoLabel>
                  <VolumeInfoValue color={tooltip.data.close >= tooltip.data.open ? '#00b35f' : '#e84142'}>
                    {tooltip.data.close.toLocaleString()}
                  </VolumeInfoValue>
                </VolumeInfoRow>
              </>
            )}
          </VolumeInfo>
        </VolumeChart>
        
        {/* Time Axis */}
        <TimeAxis>
          {timeLabels.map((label, index) => (
            <TimeLabel key={index} position={label.position}>
              {label.time}
            </TimeLabel>
          ))}
        </TimeAxis>
      </ChartContent>
    </Container>
  );
};

export default SimpleKLineChart;