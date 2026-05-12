import React from 'react';
import styled from 'styled-components';
import { BarChart3, ChevronDown, CircleDollarSign, LogOut, RadioTower, User } from 'lucide-react';
import { useMergedTickerData } from '../../hooks/useMergedTickerData';
import { useAuth } from '../../contexts/AuthContext';

const Header = styled.div`
  height: 100%;
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 10px;
  align-items: center;
  padding: 0 10px;
  background:
    linear-gradient(90deg, rgba(56, 139, 253, 0.1), transparent 48%),
    rgba(17, 24, 35, 0.96);
`;

const Left = styled.div`
  min-width: 0;
  display: flex;
  align-items: center;
  gap: 8px;
  overflow: hidden;
`;

const Brand = styled.div`
  display: inline-flex;
  align-items: center;
  gap: 6px;
  color: #00d4ff;
  font-size: 12px;
  font-weight: 900;
  letter-spacing: 0.04em;
  white-space: nowrap;
`;

const PageTag = styled.div`
  display: inline-flex;
  align-items: center;
  gap: 5px;
  height: 24px;
  padding: 0 8px;
  border-radius: 6px;
  border: 1px solid rgba(0, 212, 255, 0.16);
  background: rgba(0, 212, 255, 0.08);
  color: #7ee7ff;
  font-size: 11px;
  font-weight: 800;
  white-space: nowrap;
`;

const IconBox = styled.div`
  width: 28px;
  height: 28px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 7px;
  color: #f0b90b;
  background: rgba(240, 185, 11, 0.12);
  border: 1px solid rgba(240, 185, 11, 0.22);
  flex-shrink: 0;
`;

const SymbolSelector = styled.div`
  position: relative;
  width: 144px;
  flex-shrink: 0;
`;

const SymbolDropdown = styled.select`
  width: 100%;
  height: 30px;
  appearance: none;
  border: 1px solid rgba(87, 100, 122, 0.42);
  border-radius: 7px;
  outline: none;
  padding: 0 28px 0 10px;
  color: #f0f6fc;
  background: #0b111a;
  font-size: 14px;
  font-weight: 900;
  cursor: pointer;

  &:focus {
    border-color: #58a6ff;
  }

  option {
    background: #0b111a;
    color: #f0f6fc;
  }
`;

const DropdownIcon = styled.div`
  position: absolute;
  right: 8px;
  top: 50%;
  transform: translateY(-50%);
  color: #8b949e;
  pointer-events: none;
`;

const PriceBlock = styled.div<{ positive: boolean }>`
  display: grid;
  gap: 1px;
  min-width: 112px;
  color: ${({ positive }) => (positive ? '#3fb950' : '#f85149')};
  flex-shrink: 0;
`;

const Price = styled.div`
  font-size: 17px;
  line-height: 1;
  font-weight: 900;
  font-variant-numeric: tabular-nums;
`;

const PriceSub = styled.div`
  color: #8b949e;
  font-size: 9px;
  font-weight: 700;
`;

const Stats = styled.div`
  display: grid;
  grid-template-columns: repeat(5, minmax(74px, 1fr));
  gap: 5px;
  min-width: 0;

  @media (max-width: 1260px) {
    grid-template-columns: repeat(4, minmax(70px, 1fr));
  }
`;

const StatItem = styled.div`
  min-width: 0;
  padding: 4px 6px;
  border-radius: 6px;
  background: rgba(13, 19, 29, 0.68);
  border: 1px solid rgba(87, 100, 122, 0.24);
`;

const StatLabel = styled.div`
  color: #6e7681;
  font-size: 9px;
  font-weight: 800;
  letter-spacing: 0.04em;
`;

const StatValue = styled.div<{ tone?: 'up' | 'down' }>`
  margin-top: 2px;
  color: ${({ tone }) => (tone === 'up' ? '#3fb950' : tone === 'down' ? '#f85149' : '#d0d7de')};
  font-size: 10px;
  font-weight: 900;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
`;

const Right = styled.div`
  display: inline-flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
`;

const UserInfo = styled.div`
  display: inline-flex;
  align-items: center;
  gap: 5px;
  color: #c9d1d9;
  font-size: 11px;
  white-space: nowrap;
`;

const LogoutButton = styled.button`
  height: 28px;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 0 9px;
  border: none;
  border-radius: 6px;
  background: #ff4d4f;
  color: white;
  font-size: 11px;
  font-weight: 800;
  cursor: pointer;

  &:hover {
    background: #ff6264;
  }
`;

interface TradingHeaderProps {
  symbol: string;
  onSymbolChange: (symbol: string) => void;
}

const availableSymbols = ['BTCUSDT', 'ETHUSDT', 'SOLUSDT', 'ADAUSDT', 'BNBUSDT', 'DOGEUSDT'];

const formatNumber = (value?: number, digits = 2) => {
  if (value === undefined || !Number.isFinite(value)) return '--';
  return value.toLocaleString('en-US', {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  });
};

const formatCompact = (value?: number) => {
  if (value === undefined || !Number.isFinite(value)) return '--';
  return Intl.NumberFormat('en-US', { notation: 'compact', maximumFractionDigits: 2 }).format(value);
};

const TradingHeader: React.FC<TradingHeaderProps> = React.memo(({ symbol, onSymbolChange }) => {
  const { data, isConnected } = useMergedTickerData(symbol);
  const { user, logout } = useAuth();
  const lastPrice = data?.lastPrice || data?.midPrice;
  const changePercent = (data?.change24h ?? 0) * 100;
  const positive = changePercent >= 0;

  return (
    <Header>
      <Left>
        <Brand>CryptoSpot</Brand>
        <PageTag>
          <BarChart3 size={12} />
          交易
        </PageTag>
        <IconBox>
          <CircleDollarSign size={16} />
        </IconBox>
        <SymbolSelector>
          <SymbolDropdown value={symbol} onChange={(event) => onSymbolChange(event.target.value)}>
            {availableSymbols.map(item => (
              <option key={item} value={item}>{item}</option>
            ))}
          </SymbolDropdown>
          <DropdownIcon>
            <ChevronDown size={14} />
          </DropdownIcon>
        </SymbolSelector>
        <PriceBlock positive={positive}>
          <Price>{formatNumber(lastPrice, lastPrice && lastPrice < 10 ? 4 : 2)}</Price>
          <PriceSub>CNY {lastPrice ? formatNumber(lastPrice * 7.1, 2) : '--'}</PriceSub>
        </PriceBlock>
        <Stats>
          <StatItem>
            <StatLabel>24h 涨跌</StatLabel>
            <StatValue tone={positive ? 'up' : 'down'}>
              {data?.change24h !== undefined ? `${positive ? '+' : ''}${changePercent.toFixed(2)}%` : '--'}
            </StatValue>
          </StatItem>
          <StatItem>
            <StatLabel>24h 高</StatLabel>
            <StatValue>{formatNumber(data?.high24h, data?.high24h && data.high24h < 10 ? 4 : 2)}</StatValue>
          </StatItem>
          <StatItem>
            <StatLabel>24h 低</StatLabel>
            <StatValue>{formatNumber(data?.low24h, data?.low24h && data.low24h < 10 ? 4 : 2)}</StatValue>
          </StatItem>
          <StatItem>
            <StatLabel>24h 量</StatLabel>
            <StatValue>{formatCompact(data?.volume24h)}</StatValue>
          </StatItem>
          <StatItem>
            <StatLabel>连接</StatLabel>
            <StatValue tone={isConnected ? 'up' : undefined}>
              <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
                <RadioTower size={10} />
                {isConnected ? '实时' : '等待'}
              </span>
            </StatValue>
          </StatItem>
        </Stats>
      </Left>

      <Right>
        <UserInfo>
          <User size={13} />
          {user?.username}
        </UserInfo>
        <LogoutButton onClick={logout}>
          <LogOut size={13} />
          Logout
        </LogoutButton>
      </Right>
    </Header>
  );
});

export default TradingHeader;
