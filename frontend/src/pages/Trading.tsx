import React, { useCallback, useEffect, useRef, useState } from 'react';
import styled from 'styled-components';
import TradingHeader from '../components/trading/TradingHeader';
import { ProfessionalKLineChart } from '../components/trading/ProfessionalKLineChart';
import OrderBook from '../components/trading/OrderBook';
import TradeForm from '../components/trading/TradeForm';
import RecentTrades from '../components/trading/RecentTrades';
import AccountTabs from '../components/trading/AccountTabs';
import { useAuth } from '../contexts/AuthContext';
import { signalRClient } from '../services/signalRClient';

const TradingContainer = styled.div`
  height: 100%;
  min-height: 0;
  overflow: hidden;
  color: #e6edf3;
  background:
    radial-gradient(circle at 18% -10%, rgba(33, 111, 219, 0.18), transparent 30%),
    linear-gradient(180deg, #0b1018 0%, #090d13 100%);
`;

const MainContent = styled.div`
  height: 100%;
  display: flex;
  gap: 0;
  padding: 8px;
  min-height: 0;
`;

const LeftPanel = styled.div`
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-height: 0;
`;

const RightPanel = styled.div<{ width: number }>`
  width: ${({ width }) => width}px;
  min-width: 280px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-height: 0;
  flex-shrink: 0;

  @media (max-width: 920px) {
    width: 100% !important;
    min-width: 0;
  }
`;

const ResizeHandle = styled.div<{ isHorizontal: boolean; isDragging: boolean }>`
  flex-shrink: 0;
  background: ${({ isDragging }) => (isDragging ? 'rgba(88, 166, 255, 0.4)' : 'transparent')};
  transition: background 0.15s;
  cursor: ${({ isHorizontal }) => (isHorizontal ? 'col-resize' : 'row-resize')};
  z-index: 10;
  position: relative;
  ${({ isHorizontal }) =>
    isHorizontal
      ? 'width: 6px; margin: 0 -1px;'
      : 'height: 6px; margin: -1px 0;'}

  &:hover {
    background: rgba(88, 166, 255, 0.25);
  }

  &::after {
    content: '';
    position: absolute;
    ${({ isHorizontal }) =>
      isHorizontal
        ? 'top: 50%; left: 50%; transform: translate(-50%, -50%); width: 2px; height: 32px; border-radius: 1px;'
        : 'top: 50%; left: 50%; transform: translate(-50%, -50%); height: 2px; width: 32px; border-radius: 1px;'}
    background: rgba(139, 148, 158, 0.5);
  }
`;

const Panel = styled.section`
  min-height: 0;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  background: rgba(17, 24, 35, 0.94);
  border: 1px solid rgba(87, 100, 122, 0.38);
  border-radius: 8px;
  box-shadow: 0 12px 28px rgba(0, 0, 0, 0.22);
`;

const HeaderPanel = styled(Panel)`
  min-height: 44px;
  flex-shrink: 0;
  border-color: rgba(56, 139, 253, 0.28);
`;

const ChartPanel = styled(Panel)`
  flex: 1;
`;

const AccountPanel = styled(Panel)<{ height: number }>`
  height: ${({ height }) => height}px;
  min-height: 140px;
  flex-shrink: 0;
`;

const ResizableSection = styled.div<{ height: number }>`
  height: ${({ height }) => height}px;
  min-height: 140px;
  display: flex;
  flex-direction: column;

  @media (max-width: 920px) {
    height: auto !important;
    min-height: 220px;
  }
`;

const OrderBookSection = styled.div`
  flex: 0.88;
  min-height: 180px;
  display: flex;
  flex-direction: column;
`;

const TradesSection = styled.div`
  flex: 0.52;
  min-height: 120px;
  display: flex;
  flex-direction: column;
`;

const TradeFormSection = styled.div`
  flex: 1.3;
  min-height: 320px;
  display: flex;
  flex-direction: column;
`;

const useResizeHandle = (
  initialSize: number,
  minSize: number,
  maxSize: number,
) => {
  const [size, setSize] = useState(initialSize);
  const [isDragging, setIsDragging] = useState(false);
  const startRef = useRef({ pos: 0, size: 0 });

  const onMouseDown = useCallback(
    (e: React.MouseEvent) => {
      e.preventDefault();
      setIsDragging(true);
      startRef.current = { pos: e.clientX, size };
    },
    [size],
  );

  const onMouseDownY = useCallback(
    (e: React.MouseEvent) => {
      e.preventDefault();
      setIsDragging(true);
      startRef.current = { pos: e.clientY, size };
    },
    [size],
  );

  useEffect(() => {
    if (!isDragging) return;

    const onMove = (e: MouseEvent) => {
      const delta = e.clientX - startRef.current.pos;
      const changed = startRef.current.size + delta;
      setSize(Math.max(minSize, Math.min(maxSize, changed)));
    };

    const onUp = () => setIsDragging(false);
    document.addEventListener('mousemove', onMove);
    document.addEventListener('mouseup', onUp);
    return () => {
      document.removeEventListener('mousemove', onMove);
      document.removeEventListener('mouseup', onUp);
    };
  }, [isDragging, minSize, maxSize]);

  return { size, isDragging, onMouseDown, onMouseDownY };
};

const RIGHT_PANEL_MIN = 280;
const RIGHT_PANEL_MAX = 520;
const RIGHT_PANEL_DEFAULT = 360;

const ACCOUNT_MIN = 140;
const ACCOUNT_MAX = 400;
const ACCOUNT_DEFAULT = 220;

const Trading: React.FC = () => {
  const [selectedSymbol, setSelectedSymbol] = useState(() => {
    try {
      const params = new URLSearchParams(window.location.search);
      const urlSymbol = params.get('symbol')?.toUpperCase();
      const stored = localStorage.getItem('lastSymbol') || '';
      const candidate = urlSymbol || stored;
      if (candidate && /^[A-Z0-9]{4,15}$/.test(candidate)) return candidate;
    } catch {
      // Ignore storage and URL failures.
    }
    return 'BTCUSDT';
  });
  const [timeframe, setTimeframe] = useState('1m');
  const { user, isAuthenticated } = useAuth();

  const rightPanel = useResizeHandle(RIGHT_PANEL_DEFAULT, RIGHT_PANEL_MIN, RIGHT_PANEL_MAX);
  const accountPanel = useResizeHandle(ACCOUNT_DEFAULT, ACCOUNT_MIN, ACCOUNT_MAX);

  useEffect(() => {
    if (!isAuthenticated || !user?.id) return;

    let connection: any = null;

    const handleUserTrade = (trade: any) => {
      window.dispatchEvent(new CustomEvent('user-trade-update', { detail: trade }));
    };
    const handleOrderUpdate = (order: any) => {
      window.dispatchEvent(new CustomEvent('user-order-update', { detail: order }));
    };
    const handleAssetUpdate = (assets: any) => {
      window.dispatchEvent(new CustomEvent('user-asset-update', { detail: assets }));
    };

    const initConnection = async () => {
      try {
        const connected = await signalRClient.connect();
        if (!connected) return;

        connection = signalRClient.getConnection();
        if (!connection) return;

        await connection.invoke('SubscribeUserData');
        connection.on('UserTradeUpdate', handleUserTrade);
        connection.on('OrderUpdate', handleOrderUpdate);
        connection.on('AssetUpdate', handleAssetUpdate);
      } catch (err) {
        console.error('[Trading] Failed to initialize SignalR user stream:', err);
      }
    };

    initConnection();

    return () => {
      if (!connection) return;
      connection.invoke('UnsubscribeUserData').catch((err: any) => {
        console.error('[Trading] Failed to unsubscribe user stream:', err);
      });
      connection.off('UserTradeUpdate', handleUserTrade);
      connection.off('OrderUpdate', handleOrderUpdate);
      connection.off('AssetUpdate', handleAssetUpdate);
    };
  }, [isAuthenticated, user?.id]);

  const handleSymbolChange = (symbol: string) => {
    setSelectedSymbol(symbol);
    try {
      localStorage.setItem('lastSymbol', symbol);
      const params = new URLSearchParams(window.location.search);
      params.set('symbol', symbol);
      window.history.replaceState(null, '', `${window.location.pathname}?${params.toString()}`);
    } catch {
      // Best-effort only.
    }
  };

  return (
    <TradingContainer>
      <MainContent>
        <LeftPanel>
          <HeaderPanel>
            <TradingHeader symbol={selectedSymbol} onSymbolChange={handleSymbolChange} />
          </HeaderPanel>
          <ChartPanel>
            <ProfessionalKLineChart
              symbol={selectedSymbol}
              timeframe={timeframe}
              onTimeframeChange={setTimeframe}
            />
          </ChartPanel>
          <ResizeHandle isHorizontal={false} isDragging={accountPanel.isDragging} onMouseDown={accountPanel.onMouseDownY} />
          <AccountPanel height={accountPanel.size}>
            <AccountTabs />
          </AccountPanel>
        </LeftPanel>

        <ResizeHandle isHorizontal={true} isDragging={rightPanel.isDragging} onMouseDown={rightPanel.onMouseDown} />

        <RightPanel width={rightPanel.size}>
          <OrderBookSection>
            <Panel style={{ height: '100%' }}>
              <OrderBook symbol={selectedSymbol} />
            </Panel>
          </OrderBookSection>
          <TradesSection>
            <Panel style={{ height: '100%' }}>
              <RecentTrades symbol={selectedSymbol} />
            </Panel>
          </TradesSection>
          <TradeFormSection>
            <Panel style={{ height: '100%' }}>
              <TradeForm symbol={selectedSymbol} />
            </Panel>
          </TradeFormSection>
        </RightPanel>
      </MainContent>
    </TradingContainer>
  );
};

export default Trading;
