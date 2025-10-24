import React from 'react';
import styled from 'styled-components';
import { ChevronDown } from 'lucide-react';
import { useMergedTickerData } from '../../hooks/useMergedTickerData';

const Header = styled.div`
  height: 56px;
  background: #161b22;
  display: flex;
  align-items: center;
  /* 由 space-between 改为靠左排列，避免两侧被强制拉伸形成大空白 */
  justify-content: flex-start;
  padding: 0 0.75rem;
  position: relative;
  gap: 0.75rem;
`;

const SymbolInfo = styled.div`
  display: flex;
  align-items: center;
  gap: 0.5rem; /* 原1rem => 0.5rem 压缩 */
  min-width: 0;
`;

const SymbolSelector = styled.div`
  position: relative;
`;

const SymbolDropdown = styled.select`
  font-size: 1.25rem;
  font-weight: 600;
  background: transparent;
  border: none;
  outline: none;
  cursor: pointer;
  appearance: none;
  padding-right: 1.5rem;
  color: #f0f6fc;
  
  option {
    background: #161b22;
    color: #f0f6fc;
    padding: 0.5rem;
  }
`;

const DropdownIcon = styled.div`
  position: absolute;
  right: 0;
  top: 50%;
  transform: translateY(-50%);
  color: #7d8590;
  pointer-events: none;
  z-index: 2;
`;

const PriceInfo = styled.div`
  display: flex;
  align-items: center;
  gap: 0.5rem; /* 原2rem => 0.5rem */
`;

const PriceItem = styled.div`
  display: flex;
  flex-direction: column;
  align-items: flex-start;
`;

const Price = styled.div<{ isPositive: boolean }>`
  font-size: 1.25rem;
  font-weight: 600;
  color: ${props => props.isPositive ? '#3fb950' : '#f85149'};
  display: flex;
  align-items: center;
  /* ✅ 添加平滑颜色过渡 */
  transition: color 0.3s ease;
`;

const PriceLabel = styled.div`
  font-size: 0.8rem;
  color: #7d8590;
  font-weight: 400;
`;

const StatsGrid = styled.div`
  display: flex;
  flex-wrap: wrap;
  gap: 0.6rem 0.9rem; /* 更紧凑 */
  margin-left: 1.2rem; /* 与价格区分隔的固定间距 */
  align-items: flex-start;
  /* 允许在较窄宽度换行，但不再强制限制百分比宽度 */
  @media (max-width: 1400px) { gap: 0.5rem 0.8rem; }
  @media (max-width: 1200px) { margin-left: 0.9rem; }
  @media (max-width: 1000px) {
    margin-left: 0.6rem;
    gap: 0.45rem 0.7rem;
  }
`;

const StatItem = styled.div`
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  min-width: 70px;
`;

const StatValue = styled.div<{ color?: string }>`
  font-size: 0.9rem;
  font-weight: 600;
  color: ${props => props.color || '#f0f6fc'};
  /* ✅ 添加平滑过渡效果 */
  transition: color 0.3s ease;
  
  /* ✅ 数值更新时的闪烁动画 */
  @keyframes flash {
    0%, 100% { opacity: 1; }
    50% { opacity: 0.6; }
  }
  
  &.updating {
    animation: flash 0.3s ease;
  }
`;

const StatLabel = styled.div`
  font-size: 0.7rem;
  color: #7d8590;
  margin-top: 0.25rem;
  font-weight: 400;
  text-transform: uppercase;
  letter-spacing: 0.5px;
`;

// ✅ 拆分为独立的记忆化组件，避免整体重渲染
const Change24hStat = React.memo<{ lastPrice?: number; change24h?: number; hasPriceFrame: boolean }>(
  ({ lastPrice, change24h, hasPriceFrame }) => {
    const [isUpdating, setIsUpdating] = React.useState(false);
    const prevValueRef = React.useRef(change24h);
    
    React.useEffect(() => {
      if (prevValueRef.current !== change24h && change24h !== undefined) {
        setIsUpdating(true);
        const timer = setTimeout(() => setIsUpdating(false), 300);
        prevValueRef.current = change24h;
        return () => clearTimeout(timer);
      }
    }, [change24h]);
    
    const isPositive = (change24h ?? 0) >= 0;
    const changePercent24h = (change24h ?? 0) * 100;
    const pct = isFinite(changePercent24h) ? changePercent24h : 0;
    
    return (
      <StatItem>
        <StatValue 
          color={isPositive ? '#3fb950' : '#f85149'}
          className={isUpdating ? 'updating' : ''}
        >
          {lastPrice && lastPrice > 0 && change24h !== undefined
            ? `${isPositive ? '+' : ''}${pct.toFixed(2)}%`
            : (hasPriceFrame ? '0.00%' : '--')}
        </StatValue>
        <StatLabel>24h</StatLabel>
      </StatItem>
    );
  }
);

const High24hStat = React.memo<{ high24h?: number }>(({ high24h }) => {
  const [isUpdating, setIsUpdating] = React.useState(false);
  const prevValueRef = React.useRef(high24h);
  
  React.useEffect(() => {
    if (prevValueRef.current !== high24h && high24h !== undefined) {
      setIsUpdating(true);
      const timer = setTimeout(() => setIsUpdating(false), 300);
      prevValueRef.current = high24h;
      return () => clearTimeout(timer);
    }
  }, [high24h]);
  
  return (
    <StatItem>
      <StatValue 
        title={high24h !== undefined ? String(high24h) : ''}
        className={isUpdating ? 'updating' : ''}
      >
        {high24h !== undefined ? high24h.toFixed(high24h > 10 ? 0 : 4) : '--'}
      </StatValue>
      <StatLabel>24h高</StatLabel>
    </StatItem>
  );
});

const Low24hStat = React.memo<{ low24h?: number }>(({ low24h }) => {
  const [isUpdating, setIsUpdating] = React.useState(false);
  const prevValueRef = React.useRef(low24h);
  
  React.useEffect(() => {
    if (prevValueRef.current !== low24h && low24h !== undefined) {
      setIsUpdating(true);
      const timer = setTimeout(() => setIsUpdating(false), 300);
      prevValueRef.current = low24h;
      return () => clearTimeout(timer);
    }
  }, [low24h]);
  
  return (
    <StatItem>
      <StatValue 
        title={low24h !== undefined ? String(low24h) : ''}
        className={isUpdating ? 'updating' : ''}
      >
        {low24h !== undefined ? low24h.toFixed(low24h > 10 ? 0 : 4) : '--'}
      </StatValue>
      <StatLabel>24h低</StatLabel>
    </StatItem>
  );
});

const Volume24hStat = React.memo<{ volume24h?: number }>(({ volume24h }) => {
  const [isUpdating, setIsUpdating] = React.useState(false);
  const prevValueRef = React.useRef(volume24h);
  
  React.useEffect(() => {
    if (prevValueRef.current !== volume24h && volume24h !== undefined) {
      setIsUpdating(true);
      const timer = setTimeout(() => setIsUpdating(false), 300);
      prevValueRef.current = volume24h;
      return () => clearTimeout(timer);
    }
  }, [volume24h]);
  
  return (
    <StatItem>
      <StatValue 
        title={volume24h !== undefined ? String(volume24h) : ''}
        className={isUpdating ? 'updating' : ''}
      >
        {volume24h !== undefined 
          ? (volume24h >= 1 ? volume24h.toFixed(0) : volume24h.toFixed(2)) 
          : '--'}
      </StatValue>
      <StatLabel>24h量</StatLabel>
    </StatItem>
  );
});

// ✅ 价格信息独立组件，带更新动画
const PriceDisplay = React.memo<{ lastPrice?: number; isPositive: boolean; isConnected: boolean }>(
  ({ lastPrice, isPositive, isConnected }) => {
    const [isUpdating, setIsUpdating] = React.useState(false);
    const prevPriceRef = React.useRef(lastPrice);
    
    React.useEffect(() => {
      if (prevPriceRef.current !== lastPrice && lastPrice && lastPrice > 0) {
        setIsUpdating(true);
        const timer = setTimeout(() => setIsUpdating(false), 300);
        prevPriceRef.current = lastPrice;
        return () => clearTimeout(timer);
      }
    }, [lastPrice]);
    
    return (
      <PriceInfo>
        <PriceItem>
          <Price 
            isPositive={isPositive}
            style={{ opacity: isUpdating ? 0.6 : 1, transition: 'opacity 0.3s ease' }}
          >
            {lastPrice && lastPrice > 0 ? lastPrice.toLocaleString() : '--'}
            {isConnected && <span style={{ fontSize: '0.6rem', color: '#00b35f', marginLeft: 4 }}>●</span>}
          </Price>
          <PriceLabel>
            {lastPrice && lastPrice > 0 ? `¥${(lastPrice * 7.1).toLocaleString()}` : '¥--'}
          </PriceLabel>
        </PriceItem>
      </PriceInfo>
    );
  }
);

interface TradingHeaderProps {
  symbol: string;
  onSymbolChange: (symbol: string) => void;
}

const TradingHeader: React.FC<TradingHeaderProps> = ({ 
  symbol, 
  onSymbolChange
}) => {
  // 使用SignalR实时价格数据 (只订阅当前选中符号, 不再订阅全部以减少无关推送)
  const availableSymbols = ['BTCUSDT', 'ETHUSDT', 'SOLUSDT', 'ADAUSDT', 'BNBUSDT', 'DOGEUSDT'];
  const { data: merged, isConnected } = useMergedTickerData(symbol);
  
  // ✅ 使用 useMemo 优化计算，避免每次都重新计算
  const currentData = React.useMemo(() => 
    merged || { symbol, lastPrice: 0, change24h: 0, volume24h: 0, high24h: 0, low24h: 0, timestamp: 0 },
    [merged, symbol]
  );

  const hasPriceFrame = React.useMemo(() => 
    currentData.change24h !== undefined || currentData.volume24h !== undefined || currentData.high24h !== undefined,
    [currentData.change24h, currentData.volume24h, currentData.high24h]
  );
  
  const isPositive = React.useMemo(() => 
    (currentData.change24h ?? 0) >= 0,
    [currentData.change24h]
  );

  return (
    <Header>
      <SymbolInfo>
        <SymbolSelector>
          <SymbolDropdown 
            value={symbol} 
            onChange={(e) => onSymbolChange(e.target.value)}
          >
            {availableSymbols.map(sym => (
              <option key={sym} value={sym}>{sym}</option>
            ))}
          </SymbolDropdown>
          <DropdownIcon>
            <ChevronDown size={16} />
          </DropdownIcon>
        </SymbolSelector>
        {/* ✅ 使用独立的价格组件 */}
        <PriceDisplay 
          lastPrice={currentData.lastPrice} 
          isPositive={isPositive} 
          isConnected={isConnected}
        />
      </SymbolInfo>

      <StatsGrid>
        {/* ✅ 使用独立的记忆化组件，每个字段独立更新 */}
        <Change24hStat 
          lastPrice={currentData.lastPrice} 
          change24h={currentData.change24h} 
          hasPriceFrame={hasPriceFrame}
        />
        <High24hStat high24h={currentData.high24h} />
        <Low24hStat low24h={currentData.low24h} />
        <Volume24hStat volume24h={currentData.volume24h} />
      </StatsGrid>
    </Header>
  );
};

export default TradingHeader;