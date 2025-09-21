import React, { useState } from 'react';
import styled from 'styled-components';
import { TrendingUp, TrendingDown, Activity, Clock, BarChart3 } from 'lucide-react';
import TradingHeader from '../components/trading/TradingHeader';
import { ProfessionalKLineChart } from '../components/trading/ProfessionalKLineChart';
import OrderBook from '../components/trading/OrderBook';
import TradeForm from '../components/trading/TradeForm';
import RecentTrades from '../components/trading/RecentTrades';
import AccountTabs from '../components/trading/AccountTabs';

const TradingContainer = styled.div`
  display: grid;
  grid-template-rows: 60px 1fr;
  height: 100%;
  background: #0d1117;
  color: #f0f6fc;
  overflow: hidden;
`;

const MainContent = styled.div`
  display: grid;
  grid-template-columns: 1fr 320px;
  gap: 1px;
  background: #0d1117;
  overflow: hidden;
  min-height: 0;
`;

const LeftPanel = styled.div`
  display: grid;
  grid-template-rows: 1fr 200px;
  gap: 1px;
  background: #0d1117;
  min-height: 0;
`;

const RightPanel = styled.div`
  display: grid;
  grid-template-rows: 300px 80px 1fr;
  gap: 1px;
  background: #0d1117;
  min-height: 0;
`;

const ChartSection = styled.div`
  background: #161b22;
  border: 1px solid #30363d;
  overflow: hidden;
  display: flex;
  flex-direction: column;
`;

const BottomSection = styled.div`
  background: #161b22;
  border: 1px solid #30363d;
  overflow: hidden;
  display: flex;
  flex-direction: column;
`;

const RightPanelSection = styled.div`
  background: #161b22;
  border: 1px solid #30363d;
  overflow: hidden;
  display: flex;
  flex-direction: column;
`;

const TopRightSection = styled.div`
  background: #161b22;
  border: 1px solid #30363d;
  overflow: hidden;
  display: flex;
  flex-direction: column;
`;

const MiddleRightSection = styled.div`
  background: #161b22;
  border: 1px solid #30363d;
  overflow: hidden;
  display: flex;
  flex-direction: column;
`;

const Trading: React.FC = () => {
  const [selectedSymbol, setSelectedSymbol] = useState('BTCUSDT');
  const [timeframe, setTimeframe] = useState('1m');


  return (
    <TradingContainer>
      <TradingHeader 
        symbol={selectedSymbol}
        onSymbolChange={setSelectedSymbol}
        timeframe={timeframe}
        onTimeframeChange={setTimeframe}
      />
      
      <MainContent>
        <LeftPanel>
          <ChartSection>
            <ProfessionalKLineChart 
              symbol={selectedSymbol} 
              timeframe={timeframe} 
              onTimeframeChange={setTimeframe}
            />
          </ChartSection>
          
          <BottomSection>
            <AccountTabs />
          </BottomSection>
        </LeftPanel>
        
        <RightPanel>
          <TopRightSection>
            <OrderBook symbol={selectedSymbol} />
          </TopRightSection>
          <MiddleRightSection>
            <RecentTrades symbol={selectedSymbol} />
          </MiddleRightSection>
          <RightPanelSection>
            <TradeForm symbol={selectedSymbol} />
          </RightPanelSection>
        </RightPanel>
      </MainContent>
    </TradingContainer>
  );
};

export default Trading;
