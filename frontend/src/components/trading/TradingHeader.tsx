import React from 'react';
import styled from 'styled-components';
import { ChevronDown } from 'lucide-react';
import { useSignalRPriceData } from '../../hooks/useSignalRPriceData';

const Header = styled.div`
  height: 60px;
  background: #161b22;
  border-bottom: 1px solid #30363d;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 1rem;
  position: relative;
`;

const SymbolInfo = styled.div`
  display: flex;
  align-items: center;
  gap: 1rem;
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
  gap: 2rem;
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
`;

const PriceLabel = styled.div`
  font-size: 0.8rem;
  color: #7d8590;
  font-weight: 400;
`;

const StatsGrid = styled.div`
  display: flex;
  gap: 1.5rem;
`;

const StatItem = styled.div`
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  min-width: 80px;
`;

const StatValue = styled.div<{ color?: string }>`
  font-size: 0.9rem;
  font-weight: 600;
  color: ${props => props.color || '#f0f6fc'};
`;

const StatLabel = styled.div`
  font-size: 0.7rem;
  color: #7d8590;
  margin-top: 0.25rem;
  font-weight: 400;
  text-transform: uppercase;
  letter-spacing: 0.5px;
`;

const TimeframeBar = styled.div`
  display: flex;
  gap: 4px;
`;

const TimeframeBtn = styled.button<{active:boolean}>`
  padding:4px 8px;
  background:${p=>p.active?'#238636':'transparent'};
  border:1px solid ${p=>p.active?'#2ea043':'#30363d'};
  color:${p=>p.active?'#ffffff':'#7d8590'};
  font-size:12px;
  border-radius:4px;
  cursor:pointer;
  &:hover{border-color:#2ea043;color:#fff;}
`;

interface TradingHeaderProps {
  symbol: string;
  onSymbolChange: (symbol: string) => void;
  timeframe: string;
  onTimeframeChange: (timeframe: string) => void;
}

const TradingHeader: React.FC<TradingHeaderProps> = ({ 
  symbol, 
  onSymbolChange, 
  timeframe, 
  onTimeframeChange 
}) => {
  // 使用SignalR实时价格数据 (只订阅当前选中符号, 不再订阅全部以减少无关推送)
  const availableSymbols = ['BTCUSDT', 'ETHUSDT', 'SOLUSDT', 'ADAUSDT', 'BNBUSDT', 'DOGEUSDT'];
  const { priceData, isConnected } = useSignalRPriceData([symbol]);
  
  const currentData = priceData[symbol] || {
    symbol,
    price: 0,
    change24h: 0,
    volume24h: 0,
    high24h: 0,
    low24h: 0,
    timestamp: 0
  } as any;
  if ((window as any).__SR_DEBUG) console.log('[Header] symbol=', symbol, 'priceDataEntry=', currentData);
  
  const isPositive = (currentData.change24h || 0) >= 0;
  const changePercent24h = (currentData.change24h || 0) * 100; // 小数 -> 百分比
  const timeframes = ['1m','5m','15m','1h','4h','1d'];
  // 防止 NaN
  const pct = isFinite(changePercent24h) ? changePercent24h : 0;

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
        <PriceInfo>
          <PriceItem>
            <Price isPositive={isPositive}>
              {currentData.price > 0 ? currentData.price.toLocaleString() : '--'}
              {isConnected && <span style={{ fontSize: '0.6rem', color: '#00b35f', marginLeft: '4px' }}>●</span>}
            </Price>
            <PriceLabel>¥{currentData.price > 0 ? (currentData.price * 7.1).toLocaleString() : '--'}</PriceLabel>
          </PriceItem>
        </PriceInfo>
      </SymbolInfo>

      <StatsGrid>
        <StatItem>
          <StatValue color={isPositive ? '#3fb950' : '#f85149'}>
            {currentData.price>0?`${isPositive?'+':''}${pct.toFixed(2)}%`:'--'}
          </StatValue>
          <StatLabel>24h</StatLabel>
        </StatItem>
        <StatItem>
          <StatValue>{currentData.high24h > 0 ? currentData.high24h.toFixed(0) : '--'}</StatValue>
          <StatLabel>24h高</StatLabel>
        </StatItem>
        <StatItem>
          <StatValue>{currentData.low24h > 0 ? currentData.low24h.toFixed(0) : '--'}</StatValue>
          <StatLabel>24h低</StatLabel>
        </StatItem>
        <StatItem>
          <StatValue>{currentData.volume24h > 0 ? currentData.volume24h.toFixed(0) : '--'}</StatValue>
          <StatLabel>24h量</StatLabel>
        </StatItem>
        <TimeframeBar>
          {timeframes.map(tf=> <TimeframeBtn key={tf} active={tf===timeframe} onClick={()=>onTimeframeChange(tf)}>{tf}</TimeframeBtn>)}
        </TimeframeBar>
      </StatsGrid>
    </Header>
  );
};

export default TradingHeader;