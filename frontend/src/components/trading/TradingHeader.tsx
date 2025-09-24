import React from 'react';
import styled from 'styled-components';
import { ChevronDown } from 'lucide-react';
import { useSignalRPriceData } from '../../hooks/useSignalRPriceData';

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
  min-width: 70px; /* 略减 */
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
              {isConnected && <span style={{ fontSize: '0.6rem', color: '#00b35f', marginLeft: 4 }}>●</span>}
            </Price>
            <PriceLabel>
              {currentData.price > 0 ? `¥${(currentData.price * 7.1).toLocaleString()}` : '¥--'}
            </PriceLabel>
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
      </StatsGrid>
    </Header>
  );
};

export default TradingHeader;