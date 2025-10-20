import React, { useState, useEffect } from 'react';
import styled from 'styled-components';
import { Clock, History, CheckCircle, Wallet } from 'lucide-react';
import { useAuth } from '../../contexts/AuthContext';
import { tradingService } from '../../services/tradingService';
import { Order, Trade, Asset } from '../../types';

const Container = styled.div`
  height: 100%;
  display: flex;
  flex-direction: column;
  background: #161b22;
  border: 1px solid #30363d;
`;

const TabHeader = styled.div`
  display: flex;
  border-bottom: 1px solid #30363d;
  background: #21262d;
`;

const Tab = styled.button<{ active: boolean }>`
  flex: 1;
  padding: 0.5rem;
  background: ${props => props.active ? '#f0f6fc' : 'transparent'};
  color: ${props => props.active ? '#0d1117' : '#7d8590'};
  border: none;
  cursor: pointer;
  font-size: 0.75rem;
  font-weight: 500;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.25rem;
  transition: all 0.2s;
  
  &:hover {
    background: ${props => props.active ? '#f0f6fc' : 'rgba(255, 255, 255, 0.05)'};
    color: ${props => props.active ? '#0d1117' : '#f0f6fc'};
  }
`;

const TabContent = styled.div`
  flex: 1;
  padding: 0.75rem;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
`;

const EmptyState = styled.div`
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  flex: 1;
  color: #7d8590;
  text-align: center;
  min-height: 200px;
`;

const EmptyIcon = styled.div`
  width: 48px;
  height: 48px;
  border-radius: 50%;
  background: rgba(125, 133, 144, 0.1);
  display: flex;
  align-items: center;
  justify-content: center;
  margin-bottom: 1rem;
`;

const EmptyText = styled.div`
  font-size: 1rem;
  font-weight: 600;
  color: #f0f6fc;
  margin-bottom: 0.5rem;
`;

const EmptySubtext = styled.div`
  font-size: 0.8rem;
  color: #7d8590;
`;

const AuthenticatedContent = styled.div`
  display: flex;
  flex-direction: column;
  height: 100%;
  color: #f0f6fc;
`;

const DataTable = styled.div`
  flex: 1;
  display: flex;
  flex-direction: column;
  background: #0d1117;
  border-radius: 6px;
  border: 1px solid #30363d;
  overflow: hidden;
`;

const TableHeader = styled.div`
  display: grid;
  grid-template-columns: 1fr 1fr 1fr 1fr;
  padding: 0.75rem;
  background: #21262d;
  border-bottom: 1px solid #30363d;
  font-size: 0.7rem;
  font-weight: 600;
  color: #7d8590;
  text-transform: uppercase;
  letter-spacing: 0.5px;
`;

const TableBody = styled.div`
  flex: 1;
  overflow-y: auto;
`;

const TableRow = styled.div`
  display: grid;
  grid-template-columns: 1fr 1fr 1fr 1fr;
  padding: 0.75rem;
  border-bottom: 1px solid #21262d;
  font-size: 0.8rem;
  transition: background-color 0.2s;
  
  &:hover {
    background: #21262d;
  }
  
  &:last-child {
    border-bottom: none;
  }
`;

const StatusBadge = styled.span<{ status: 'pending' | 'filled' | 'cancelled' }>`
  padding: 0.2rem 0.4rem;
  border-radius: 8px;
  font-size: 0.7rem;
  font-weight: 600;
  text-transform: uppercase;
  background: ${props => {
    switch (props.status) {
      case 'pending': return 'rgba(255, 165, 0, 0.2)';
      case 'filled': return 'rgba(63, 185, 80, 0.2)';
      case 'cancelled': return 'rgba(248, 81, 73, 0.2)';
      default: return 'rgba(125, 133, 144, 0.2)';
    }
  }};
  color: ${props => {
    switch (props.status) {
      case 'pending': return '#ffa500';
      case 'filled': return '#3fb950';
      case 'cancelled': return '#f85149';
      default: return '#7d8590';
    }
  }};
`;

const AccountTabs: React.FC = () => {
  const [activeTab, setActiveTab] = useState<'current' | 'history' | 'trades' | 'assets'>('current');
  const { user } = useAuth();

  const [orders, setOrders] = useState<Order[]>([]);
  const [openOrders, setOpenOrders] = useState<Order[]>([]);
  const [trades, setTrades] = useState<Trade[]>([]);
  const [assets, setAssets] = useState<Asset[]>([]);
  const [loading, setLoading] = useState(false);

  const loadData = async () => {
    if (!user) return;
    setLoading(true);
    try {
      const [all, open, tradeList, assetList] = await Promise.all([
        tradingService.getUserOrders().catch(() => []),
        tradingService.getOpenOrders().catch(() => []),
        tradingService.getUserTrades().catch(() => []),
        tradingService.getUserAssets().catch(() => [])
      ]);
      setOrders(all);
      setOpenOrders(open.length ? open : all.filter(o => ['pending','active','partial'].includes(o.status)));
      setTrades(tradeList);
      setAssets(assetList);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { loadData(); }, [user]);
  
  // 监听实时推送事件
  useEffect(() => {
    const handleUserTradeUpdate = (event: Event) => {
      const trade = (event as CustomEvent).detail;
      console.log('📊 [AccountTabs] 收到用户成交推送事件:', trade);
      setTrades(prev => [trade, ...prev].slice(0, 50)); // 保留最新50条
      loadData(); // 全量刷新以确保数据一致性
    };

    const handleOrderUpdate = (event: Event) => {
      const order = (event as CustomEvent).detail;
      console.log('📝 [AccountTabs] 收到订单更新推送事件:', order);
      loadData(); // 订单状态变化需要全量刷新
    };

    const handleAssetUpdate = (event: Event) => {
      const assets = (event as CustomEvent).detail;
      console.log('💰 [AccountTabs] 收到资产更新推送事件:', assets);
      if (Array.isArray(assets)) {
        setAssets(assets);
      } else {
        loadData(); // 如果格式不对就全量刷新
      }
    };

    window.addEventListener('user-trade-update', handleUserTradeUpdate);
    window.addEventListener('user-order-update', handleOrderUpdate);
    window.addEventListener('user-asset-update', handleAssetUpdate);

    console.log('👂 [AccountTabs] 已注册自定义事件监听器');

    return () => {
      window.removeEventListener('user-trade-update', handleUserTradeUpdate);
      window.removeEventListener('user-order-update', handleOrderUpdate);
      window.removeEventListener('user-asset-update', handleAssetUpdate);
      console.log('🧹 [AccountTabs] 已移除事件监听器');
    };
  }, []);

  // 定期轮询作为备份机制
  useEffect(() => { const id = setInterval(loadData, 30000); return () => clearInterval(id); }, [user]);

  const statusMap: Record<string, 'pending' | 'active' | 'partial' | 'filled' | 'cancelled'> = {
    Pending: 'pending',
    Active: 'active',
    PartiallyFilled: 'partial',
    Filled: 'filled',
    Cancelled: 'cancelled'
  };

  const renderStatusBadge = (s: string) => {
    const mapped = statusMap[s] || 'pending';
    return <StatusBadge status={mapped === 'filled' ? 'filled' : mapped === 'cancelled' ? 'cancelled' : 'pending'}>{mapped}</StatusBadge>;
  };

  const renderTabContent = () => {
    if (!user) {
      return (
        <EmptyState>
          <EmptyIcon>
            <Wallet size={24} />
          </EmptyIcon>
          <EmptyText>请先登录</EmptyText>
          <EmptySubtext>登录后查看您的交易记录和资产</EmptySubtext>
        </EmptyState>
      );
    }

    switch (activeTab) {
      case 'current': {
        const list = openOrders;
        return (
          <AuthenticatedContent>
            <DataTable>
              <TableHeader>
                <div>交易对</div>
                <div>方向/类型</div>
                <div>价格</div>
                <div>状态</div>
              </TableHeader>
              <TableBody>
                {list.length > 0 ? list.map(o => (
                  <TableRow key={o.id}>
                    <div>{o.symbol}</div>
                    <div style={{ color: o.side === 'buy' ? '#3fb950' : '#f85149' }}>{o.side}/{o.type}</div>
                    <div>{o.price ?? '-'}</div>
                    <div>{renderStatusBadge(o.status)}</div>
                  </TableRow>
                )) : (
                  <TableRow>
                    <div style={{ gridColumn: '1 / -1', textAlign: 'center', color: '#7d8590', padding: '20px' }}>
                      {loading ? '加载中...' : '暂无当前订单数据'}
                    </div>
                  </TableRow>
                )}
              </TableBody>
            </DataTable>
          </AuthenticatedContent>
        );
      }
      case 'history': {
        const history = orders.filter(o => ['filled','cancelled'].includes(o.status));
        return (
          <AuthenticatedContent>
            <DataTable>
              <TableHeader>
                <div>交易对</div>
                <div>方向/类型</div>
                <div>数量/成交</div>
                <div>时间</div>
              </TableHeader>
              <TableBody>
                {history.length > 0 ? history.map(o => (
                  <TableRow key={o.id}>
                    <div>{o.symbol}</div>
                    <div style={{ color: o.side === 'buy' ? '#3fb950' : '#f85149' }}>{o.side}/{o.type}</div>
                    <div>{o.filledQuantity ?? 0}/{o.quantity}</div>
                    <div>{new Date(o.createdAt).toLocaleTimeString()}</div>
                  </TableRow>
                )) : (
                  <TableRow>
                    <div style={{ gridColumn: '1 / -1', textAlign: 'center', color: '#7d8590', padding: '20px' }}>
                      {loading ? '加载中...' : '暂无历史订单数据'}
                    </div>
                  </TableRow>
                )}
              </TableBody>
            </DataTable>
          </AuthenticatedContent>
        );
      }
      case 'trades': {
        return (
          <AuthenticatedContent>
            <DataTable>
              <TableHeader>
                <div>交易对</div>
                <div>方向</div>
                <div>价格/数量</div>
                <div>时间</div>
              </TableHeader>
              <TableBody>
                {trades.length > 0 ? trades.map(t => (
                  <TableRow key={t.id}>
                    <div>{t.symbol}</div>
                    <div style={{ color: t.side === 'buy' ? '#3fb950' : '#f85149' }}>{t.side || '-'}</div>
                    <div>{t.price} / {t.quantity}</div>
                    <div>{new Date(t.executedAt).toLocaleTimeString()}</div>
                  </TableRow>
                )) : (
                  <TableRow>
                    <div style={{ gridColumn: '1 / -1', textAlign: 'center', color: '#7d8590', padding: '20px' }}>
                      {loading ? '加载中...' : '暂无成交记录数据'}
                    </div>
                  </TableRow>
                )}
              </TableBody>
            </DataTable>
          </AuthenticatedContent>
        );
      }
      case 'assets': {
        return (
          <AuthenticatedContent>
            <DataTable>
              <TableHeader>
                <div>币种</div>
                <div>可用</div>
                <div>冻结</div>
                <div>总额</div>
              </TableHeader>
              <TableBody>
                {assets.length > 0 ? assets.map(a => (
                  <TableRow key={a.symbol}>
                    <div style={{ fontWeight: 'bold' }}>{a.symbol}</div>
                    <div>{a.available}</div>
                    <div>{a.frozen}</div>
                    <div>{a.total}</div>
                  </TableRow>
                )) : (
                  <TableRow>
                    <div style={{ gridColumn: '1 / -1', textAlign: 'center', color: '#7d8590', padding: '20px' }}>
                      {loading ? '加载中...' : '暂无资产数据'}
                    </div>
                  </TableRow>
                )}
              </TableBody>
            </DataTable>
          </AuthenticatedContent>
        );
      }
      default:
        return null;
    }
  };

  return (
    <Container>
      <TabHeader>
        <Tab 
          active={activeTab === 'current'} 
          onClick={() => setActiveTab('current')}
        >
          <Clock size={14} />
          当前委托
        </Tab>
        <Tab 
          active={activeTab === 'history'} 
          onClick={() => setActiveTab('history')}
        >
          <History size={14} />
          历史委托
        </Tab>
        <Tab 
          active={activeTab === 'trades'} 
          onClick={() => setActiveTab('trades')}
        >
          <CheckCircle size={14} />
          成交记录
        </Tab>
        <Tab 
          active={activeTab === 'assets'} 
          onClick={() => setActiveTab('assets')}
        >
          <Wallet size={14} />
          我的资产
        </Tab>
      </TabHeader>
      
      <TabContent>
        {renderTabContent()}
      </TabContent>
    </Container>
  );
};

export default AccountTabs;