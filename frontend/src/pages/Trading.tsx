import React, { useEffect, useState } from 'react';
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
  display: grid;
  grid-template-columns: minmax(0, 1fr) 320px;
  gap: 8px;
  padding: 8px;
  min-height: 0;

  @media (max-width: 1180px) {
    grid-template-columns: minmax(0, 1fr) 292px;
  }

  @media (max-width: 920px) {
    overflow-y: auto;
    grid-template-columns: 1fr;
    grid-auto-rows: auto;
  }
`;

const LeftPanel = styled.div`
  display: grid;
  grid-template-rows: 44px minmax(420px, 1fr) 156px;
  gap: 8px;
  min-width: 0;
  min-height: 0;

  @media (max-width: 920px) {
    grid-template-rows: 72px 460px 220px;
  }
`;

const RightPanel = styled.div`
  display: grid;
  grid-template-rows: minmax(246px, 0.88fr) minmax(150px, 0.52fr) minmax(400px, 1.3fr);
  gap: 8px;
  min-width: 0;
  min-height: 0;

  @media (max-width: 920px) {
    grid-template-rows: 380px 260px 420px;
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
  border-color: rgba(56, 139, 253, 0.28);
`;

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

        await connection.invoke('SubscribeUserData', user.id);
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
      connection.invoke('UnsubscribeUserData', user.id).catch((err: any) => {
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
          <Panel>
            <ProfessionalKLineChart
              symbol={selectedSymbol}
              timeframe={timeframe}
              onTimeframeChange={setTimeframe}
            />
          </Panel>
          <Panel>
            <AccountTabs />
          </Panel>
        </LeftPanel>

        <RightPanel>
          <Panel>
            <OrderBook symbol={selectedSymbol} />
          </Panel>
          <Panel>
            <RecentTrades symbol={selectedSymbol} />
          </Panel>
          <Panel>
            <TradeForm symbol={selectedSymbol} />
          </Panel>
        </RightPanel>
      </MainContent>
    </TradingContainer>
  );
};

export default Trading;
