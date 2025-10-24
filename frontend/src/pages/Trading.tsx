import React, { useState, useEffect } from 'react';
import styled from 'styled-components';
import { TrendingUp, TrendingDown, Activity, Clock, BarChart3 } from 'lucide-react';
import TradingHeader from '../components/trading/TradingHeader';
import { ProfessionalKLineChart } from '../components/trading/ProfessionalKLineChart';
import OrderBook from '../components/trading/OrderBook';
import TradeForm from '../components/trading/TradeForm';
import RecentTrades from '../components/trading/RecentTrades';
import AccountTabs from '../components/trading/AccountTabs';
import { useAuth } from '../contexts/AuthContext';
import { signalRClient } from '../services/signalRClient';

const TradingContainer = styled.div`
  display: grid;
  /* 移除原先顶部 60px Header 行, 让内容直接紧贴全局导航栏 */
  grid-template-rows: 1fr;
  height: 100%;
  background: #0d1117;
  color: #f0f6fc;
  overflow: hidden;
`;

const MainContent = styled.div`
  display: grid;
  /* 调整：使用固定 260px 右侧栏 + 自适应左侧 (较原320缩小，避免表单被压缩到只剩按钮) */
  grid-template-columns: 1fr 260px;
  gap: 1px;
  background: #0d1117;
  overflow: hidden;
  min-height: 0;
  @media (max-width: 1300px) {
    /* 窄屏再进一步压缩，但保持>=230 保障输入框显示 */
    grid-template-columns: 1fr 230px;
  }
`;

const LeftPanel = styled.div`
  display: grid;
  /* 新增第一行用于紧凑 Header, 其后是图表与账户区域 */
  grid-template-rows: 56px 1fr 200px;
  gap: 1px;
  background: #0d1117;
  min-height: 0;
`;

const RightPanel = styled.div`
  display: grid;
  /* 原: 300px 80px 1fr -> 调整为 1fr 180px 240px 让订单簿自动撑满上方，提高其相对位置 */
  grid-template-rows: 1fr 180px 240px;
  gap: 1px;
  background: #0d1117;
  min-height: 0;
  min-width: 230px; /* 确保表单最小展示宽度 */
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
  // 支持从 URL (?symbol=XXX) 或 localStorage 还原用户上次选择的交易对，默认回退 BTCUSDT
  const [selectedSymbol, setSelectedSymbol] = useState(() => {
    try {
      const params = new URLSearchParams(window.location.search);
      const urlSymbol = params.get('symbol')?.toUpperCase();
      const stored = localStorage.getItem('lastSymbol') || '';
      const candidate = urlSymbol || stored;
      // 简单合法性校验（可根据可用列表再严一点）
      if (candidate && /^[A-Z0-9]{4,15}$/.test(candidate)) return candidate;
    } catch {}
    return 'BTCUSDT';
  });
  const [timeframe, setTimeframe] = useState('1m');
  const { user, isAuthenticated } = useAuth();

  // 订阅用户数据推送
  useEffect(() => {
    if (!isAuthenticated || !user?.id) {
      return;
    }
    
    let connection: any = null;
    
    // 监听用户成交更新
    const handleUserTrade = (trade: any) => {
      window.dispatchEvent(new CustomEvent('user-trade-update', { detail: trade }));
    };

    // 监听订单状态更新
    const handleOrderUpdate = (order: any) => {
      window.dispatchEvent(new CustomEvent('user-order-update', { detail: order }));
    };

    // 监听资产更新
    const handleAssetUpdate = (assets: any) => {
      window.dispatchEvent(new CustomEvent('user-asset-update', { detail: assets }));
    };
    
    // 先建立SignalR连接
    const initConnection = async () => {
      try {
        const connected = await signalRClient.connect();
        if (!connected) {
          console.error('❌ [Trading] SignalR连接失败');
          return;
        }
        
        connection = signalRClient.getConnection();
        if (!connection) {
          console.error('❌ [Trading] SignalR连接对象不存在');
          return;
        }

        // 订阅用户数据组
        await connection.invoke('SubscribeUserData', user.id);

        // 注册事件监听器
        connection.on('UserTradeUpdate', handleUserTrade);
        connection.on('OrderUpdate', handleOrderUpdate);
        connection.on('AssetUpdate', handleAssetUpdate);
      } catch (err) {
        console.error('❌ [Trading] 初始化SignalR失败:', err);
      }
    };

    initConnection();

    // 清理函数
    return () => {
      if (connection) {
        connection.invoke('UnsubscribeUserData', user.id).catch((err: any) => {
          console.error('❌ [Trading] 取消订阅失败:', err);
        });
        connection.off('UserTradeUpdate', handleUserTrade);
        connection.off('OrderUpdate', handleOrderUpdate);
        connection.off('AssetUpdate', handleAssetUpdate);
      }
    };
  }, [isAuthenticated, user?.id]);

  React.useEffect(()=>{
    // 临时开启调试
    (window as any).__SR_DEBUG = true;
    return ()=>{ delete (window as any).__SR_DEBUG; };
  },[]);

  return (
    <TradingContainer>
      {/* 顶部Header已移除并内嵌到左侧面板 */}
      <MainContent>
        <LeftPanel>
          <div style={{ background:'#161b22', border:'1px solid #30363d', overflow:'hidden' }}>
            <TradingHeader 
              symbol={selectedSymbol}
              onSymbolChange={(sym: string) => {
                setSelectedSymbol(sym);
                try { localStorage.setItem('lastSymbol', sym); } catch {}
                // 同步更新 URL (保持其它查询参数) - 可选
                try {
                  const params = new URLSearchParams(window.location.search);
                  params.set('symbol', sym);
                  const newUrl = `${window.location.pathname}?${params.toString()}`;
                  window.history.replaceState(null, '', newUrl);
                } catch {}
              }}
            />
          </div>
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
