import React, { useMemo } from 'react';
import styled from 'styled-components';
import { Layers, Wifi, WifiOff } from 'lucide-react';
import { useSignalRPriceData } from '../../hooks/useSignalRPriceData';
import { useSignalROrderBook, OrderBookLevel } from '../../hooks/useSignalROrderBook';
import { useSignalRTicker } from '../../hooks/useSignalRTicker';

const Container = styled.div`
  height: 100%;
  min-height: 0;
  display: flex;
  flex-direction: column;
  background: #111823;
`;

const Header = styled.div`
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 8px 10px;
  border-bottom: 1px solid rgba(87, 100, 122, 0.34);
`;

const Title = styled.div`
  display: flex;
  align-items: center;
  gap: 8px;
  color: #f0f6fc;
  font-size: 13px;
  font-weight: 800;
`;

const Status = styled.div<{ connected: boolean }>`
  display: inline-flex;
  align-items: center;
  gap: 5px;
  font-size: 11px;
  font-weight: 700;
  color: ${({ connected }) => (connected ? '#3fb950' : '#8b949e')};
`;

const HeaderRow = styled.div`
  display: grid;
  grid-template-columns: 1.1fr 0.95fr 1fr;
  gap: 8px;
  padding: 6px 10px;
  color: #6e7681;
  font-size: 11px;
  font-weight: 800;
  letter-spacing: 0.04em;
  border-bottom: 1px solid rgba(87, 100, 122, 0.24);
`;

const Content = styled.div`
  flex: 1;
  min-height: 0;
  display: grid;
  grid-template-rows: 1fr auto 1fr;
  overflow: hidden;
`;

const BookSide = styled.div`
  min-height: 0;
  overflow-y: auto;
  overscroll-behavior: contain;
`;

const PriceRow = styled.div<{ side: 'buy' | 'sell' }>`
  position: relative;
  display: grid;
  grid-template-columns: 1.1fr 0.95fr 1fr;
  gap: 8px;
  align-items: center;
  height: 22px;
  padding: 0 10px;
  font-size: 12px;
  font-variant-numeric: tabular-nums;
  overflow: hidden;

  &:hover {
    background: rgba(88, 166, 255, 0.08);
  }
`;

const DepthBar = styled.div<{ side: 'buy' | 'sell'; percentage: number }>`
  position: absolute;
  inset: 2px 0 2px auto;
  width: ${({ percentage }) => percentage}%;
  border-radius: 5px 0 0 5px;
  background: ${({ side }) =>
    side === 'buy'
      ? 'linear-gradient(90deg, rgba(63, 185, 80, 0.02), rgba(63, 185, 80, 0.2))'
      : 'linear-gradient(90deg, rgba(248, 81, 73, 0.02), rgba(248, 81, 73, 0.2))'};
`;

const PriceCell = styled.div<{ side: 'buy' | 'sell' }>`
  position: relative;
  z-index: 1;
  color: ${({ side }) => (side === 'buy' ? '#3fb950' : '#f85149')};
  font-weight: 800;
`;

const AmountCell = styled.div`
  position: relative;
  z-index: 1;
  color: #d0d7de;
  text-align: right;
`;

const TotalCell = styled.div`
  position: relative;
  z-index: 1;
  color: #8b949e;
  text-align: right;
`;

const CurrentPrice = styled.div<{ positive: boolean }>`
  display: grid;
  gap: 3px;
  place-items: center;
  padding: 8px 10px;
  border-block: 1px solid rgba(87, 100, 122, 0.34);
  background:
    linear-gradient(90deg, rgba(248, 81, 73, 0.08), transparent 35%, transparent 65%, rgba(63, 185, 80, 0.08)),
    #0d131d;
`;

const PriceValue = styled.div<{ positive: boolean }>`
  color: ${({ positive }) => (positive ? '#3fb950' : '#f85149')};
  font-size: 18px;
  line-height: 1;
  font-weight: 900;
  font-variant-numeric: tabular-nums;
`;

const SpreadValue = styled.div`
  color: #8b949e;
  font-size: 11px;
  font-weight: 700;
`;

const EmptyState = styled.div`
  height: 100%;
  display: grid;
  place-items: center;
  color: #8b949e;
  font-size: 12px;
  text-align: center;
  padding: 20px;
`;

interface OrderBookProps {
  symbol: string;
}

const formatPrice = (value: number) => value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const formatQty = (value: number) => value >= 1 ? value.toFixed(4) : value.toFixed(8);

const OrderBook: React.FC<OrderBookProps> = React.memo(({ symbol }) => {
  const { priceData } = useSignalRPriceData([symbol]);
  const { orderBookData, loading, error, isConnected } = useSignalROrderBook(symbol);
  const { tickerData } = useSignalRTicker(symbol);

  const { buyOrders, sellOrders } = useMemo(() => {
    if (!orderBookData) return { buyOrders: [], sellOrders: [] };
    return {
      buyOrders: (orderBookData.bids || []).slice(0, 16),
      sellOrders: (orderBookData.asks || []).slice(0, 16),
    };
  }, [orderBookData]);

  const currentPrice = useMemo(() => {
    const basePrice = priceData[symbol]?.price || 0;
    if (tickerData?.lastPrice && tickerData.lastPrice > 0) return tickerData.lastPrice;
    if (tickerData?.midPrice && tickerData.midPrice > 0) return tickerData.midPrice;
    if (basePrice > 0) return basePrice;
    if (buyOrders.length > 0 && sellOrders.length > 0) return (buyOrders[0].price + sellOrders[0].price) / 2;
    return buyOrders[0]?.price || sellOrders[0]?.price || 0;
  }, [buyOrders, priceData, sellOrders, symbol, tickerData]);

  const spread = sellOrders[0] && buyOrders[0] ? sellOrders[0].price - buyOrders[0].price : 0;
  const maxTotal = Math.max(
    ...buyOrders.map(order => order.total),
    ...sellOrders.map(order => order.total),
    1
  );
  const hasData = buyOrders.length > 0 || sellOrders.length > 0;
  const positive = true;

  const renderRows = (orders: OrderBookLevel[], side: 'buy' | 'sell') => {
    const ordered = side === 'sell' ? [...orders].reverse() : orders;
    return ordered.map(order => (
      <PriceRow key={`${side}-${order.price}`} side={side}>
        <DepthBar side={side} percentage={Math.min(100, order.total / maxTotal * 100)} />
        <PriceCell side={side}>{formatPrice(order.price)}</PriceCell>
        <AmountCell>{formatQty(order.amount)}</AmountCell>
        <TotalCell>{formatQty(order.total)}</TotalCell>
      </PriceRow>
    ));
  };

  return (
    <Container>
      <Header>
        <Title>
          <Layers size={16} />
          盘口深度
        </Title>
        <Status connected={isConnected}>
          {isConnected ? <Wifi size={13} /> : <WifiOff size={13} />}
          {isConnected ? '实时' : '等待'}
        </Status>
      </Header>

      <HeaderRow>
        <div>价格(USDT)</div>
        <div style={{ textAlign: 'right' }}>数量</div>
        <div style={{ textAlign: 'right' }}>累计</div>
      </HeaderRow>

      <Content>
        {hasData ? (
          <>
            <BookSide>{renderRows(sellOrders, 'sell')}</BookSide>
            <CurrentPrice positive={positive}>
              <PriceValue positive={positive}>{currentPrice > 0 ? formatPrice(currentPrice) : '--'}</PriceValue>
              <SpreadValue>Spread {spread > 0 ? formatPrice(spread) : '--'}</SpreadValue>
            </CurrentPrice>
            <BookSide>{renderRows(buyOrders, 'buy')}</BookSide>
          </>
        ) : (
          <EmptyState>
            {loading ? '正在加载盘口数据...' : error ? '盘口连接失败' : '等待盘口数据...'}
          </EmptyState>
        )}
      </Content>
    </Container>
  );
});

export default OrderBook;
