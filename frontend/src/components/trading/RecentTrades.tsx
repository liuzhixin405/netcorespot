import React, { useEffect, useMemo, useState } from 'react';
import styled from 'styled-components';
import { Activity, Filter } from 'lucide-react';
import { tradingApi } from '../../api/trading';
import { signalRClient } from '../../services/signalRClient';

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
  gap: 8px;
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

const DustButton = styled.button<{ active: boolean }>`
  height: 26px;
  display: inline-flex;
  align-items: center;
  gap: 5px;
  padding: 0 9px;
  border-radius: 6px;
  border: 1px solid ${({ active }) => (active ? 'rgba(88, 166, 255, 0.42)' : 'rgba(87, 100, 122, 0.36)')};
  background: ${({ active }) => (active ? 'rgba(88, 166, 255, 0.12)' : '#0b111a')};
  color: ${({ active }) => (active ? '#79c0ff' : '#8b949e')};
  font-size: 11px;
  font-weight: 800;
  cursor: pointer;
`;

const ColumnHeader = styled.div`
  display: grid;
  grid-template-columns: 0.92fr 1fr 1fr;
  gap: 8px;
  padding: 6px 10px;
  color: #6e7681;
  font-size: 11px;
  font-weight: 800;
  letter-spacing: 0.04em;
  border-bottom: 1px solid rgba(87, 100, 122, 0.24);
`;

const TradesList = styled.div`
  flex: 1;
  min-height: 0;
  overflow-y: auto;
`;

const TradeRow = styled.div<{ buy: boolean; muted?: boolean }>`
  display: grid;
  grid-template-columns: 0.92fr 1fr 1fr;
  gap: 8px;
  align-items: center;
  min-height: 22px;
  padding: 0 10px;
  font-size: 12px;
  font-variant-numeric: tabular-nums;
  opacity: ${({ muted }) => (muted ? 0.58 : 1)};
  background: ${({ buy }) => (buy ? 'linear-gradient(90deg, rgba(63,185,80,0.05), transparent 55%)' : 'linear-gradient(90deg, rgba(248,81,73,0.05), transparent 55%)')};

  &:hover {
    background: rgba(88, 166, 255, 0.08);
  }
`;

const TimeColumn = styled.div`
  color: #8b949e;
`;

const PriceColumn = styled.div<{ buy: boolean }>`
  color: ${({ buy }) => (buy ? '#3fb950' : '#f85149')};
  font-weight: 900;
  text-align: right;
`;

const AmountColumn = styled.div`
  color: #d0d7de;
  text-align: right;
`;

const EmptyState = styled.div`
  height: 100%;
  display: grid;
  place-items: center;
  padding: 20px;
  color: #8b949e;
  font-size: 12px;
  text-align: center;
`;

interface RecentTradesProps {
  symbol: string;
}

interface Trade {
  id: number | string;
  symbol: string;
  price: number;
  quantity: number;
  executedAt: string;
  isBuyerMaker: boolean;
}

interface TradingPairMeta {
  quantityPrecision: number;
  minQuantity: number;
  pricePrecision: number;
}

const inferPairMeta = (symbol: string): TradingPairMeta => {
  if (symbol.startsWith('BTC')) return { quantityPrecision: 5, minQuantity: 0.00001, pricePrecision: 2 };
  if (symbol.startsWith('ETH')) return { quantityPrecision: 4, minQuantity: 0.0001, pricePrecision: 2 };
  if (symbol.startsWith('SOL')) return { quantityPrecision: 3, minQuantity: 0.001, pricePrecision: 3 };
  return { quantityPrecision: 4, minQuantity: 0.0001, pricePrecision: 2 };
};

const RecentTrades: React.FC<RecentTradesProps> = React.memo(({ symbol }) => {
  const [trades, setTrades] = useState<Trade[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [pairMeta, setPairMeta] = useState<TradingPairMeta>(() => inferPairMeta(symbol));
  const [hideDust, setHideDust] = useState(true);

  useEffect(() => {
    setTrades([]);
    setPairMeta(inferPairMeta(symbol));

    const fetchTrades = async () => {
      try {
        setLoading(true);
        setError(null);
        const [pair, recentTrades] = await Promise.all([
          tradingApi.getTradingPairs().catch(() => []),
          tradingApi.getRecentTrades(symbol, 80),
        ]);
        const currentPair = pair.find(item => item.symbol === symbol);
        if (currentPair) {
          setPairMeta({
            quantityPrecision: currentPair.quantityPrecision ?? inferPairMeta(symbol).quantityPrecision,
            minQuantity: currentPair.minQuantity ?? inferPairMeta(symbol).minQuantity,
            pricePrecision: currentPair.pricePrecision ?? inferPairMeta(symbol).pricePrecision,
          });
        }
        setTrades(recentTrades.map((trade: any) => ({
          id: trade.id,
          symbol: trade.symbol,
          price: Number(trade.price),
          quantity: Number(trade.quantity),
          executedAt: trade.executedAt,
          isBuyerMaker: Boolean(trade.isBuyerMaker),
        })));
      } catch (err) {
        setError('加载成交失败');
      } finally {
        setLoading(false);
      }
    };

    fetchTrades();
  }, [symbol]);

  useEffect(() => {
    let unsubscribe: (() => void) | null = null;
    let activeSymbol = symbol;

    const setupSignalR = async () => {
      try {
        unsubscribe = await signalRClient.subscribeTrades(
          symbol,
          (trade: any) => {
            if (trade.symbol !== activeSymbol) return;
            setTrades(prev => [{
              id: trade.id,
              symbol: trade.symbol,
              price: Number(trade.price),
              quantity: Number(trade.quantity),
              executedAt: trade.executedAt,
              isBuyerMaker: Boolean(trade.isBuyerMaker),
            }, ...prev].slice(0, 80));
          },
          () => undefined
        );
      } catch (err) {
        // Keep the HTTP snapshot visible if realtime subscription fails.
      }
    };

    setupSignalR();
    return () => {
      activeSymbol = '';
      unsubscribe?.();
    };
  }, [symbol]);

  const dustThreshold = pairMeta.minQuantity * 0.1;
  const displayedTrades = useMemo(
    () => trades.filter(trade => !hideDust || trade.quantity >= dustThreshold),
    [dustThreshold, hideDust, trades]
  );
  const hiddenCount = trades.length - displayedTrades.length;

  const formatTime = (dateStr: string) => {
    const date = new Date(dateStr);
    return date.toLocaleTimeString('zh-CN', {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: false,
    });
  };

  const formatQty = (quantity: number) => {
    if (quantity > 0 && quantity < 1 / Math.pow(10, pairMeta.quantityPrecision)) {
      return `<${(1 / Math.pow(10, pairMeta.quantityPrecision)).toFixed(pairMeta.quantityPrecision)}`;
    }
    return quantity.toFixed(pairMeta.quantityPrecision);
  };

  return (
    <Container>
      <Header>
        <Title>
          <Activity size={16} />
          实时成交
        </Title>
        <DustButton type="button" active={hideDust} onClick={() => setHideDust(value => !value)}>
          <Filter size={13} />
          {hideDust ? `过滤微尘${hiddenCount > 0 ? ` ${hiddenCount}` : ''}` : '显示全部'}
        </DustButton>
      </Header>

      <ColumnHeader>
        <div>时间</div>
        <div style={{ textAlign: 'right' }}>价格</div>
        <div style={{ textAlign: 'right' }}>数量</div>
      </ColumnHeader>

      <TradesList>
        {loading && trades.length === 0 ? (
          <EmptyState>正在加载成交数据...</EmptyState>
        ) : error ? (
          <EmptyState>{error}</EmptyState>
        ) : displayedTrades.length > 0 ? (
          displayedTrades.map(trade => {
            const buy = !trade.isBuyerMaker;
            const dust = trade.quantity < dustThreshold;
            return (
              <TradeRow
                key={`${trade.id}-${trade.executedAt}`}
                buy={buy}
                muted={dust}
                title={`价格 ${trade.price} / 数量 ${trade.quantity}`}
              >
                <TimeColumn>{formatTime(trade.executedAt)}</TimeColumn>
                <PriceColumn buy={buy}>{trade.price.toFixed(pairMeta.pricePrecision)}</PriceColumn>
                <AmountColumn>{formatQty(trade.quantity)}</AmountColumn>
              </TradeRow>
            );
          })
        ) : (
          <EmptyState>暂无成交数据</EmptyState>
        )}
      </TradesList>
    </Container>
  );
});

export default RecentTrades;
