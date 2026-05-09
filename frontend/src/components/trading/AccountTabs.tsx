import React, { useState, useEffect } from 'react';
import styled from 'styled-components';
import { Clock, History, CheckCircle, Wallet } from 'lucide-react';
import { useAuth } from '../../contexts/AuthContext';
import { tradingService } from '../../services/tradingService';
import { useUserDataStream } from '../../hooks/useUserDataStream';
import { Order, Trade, Asset } from '../../types';

const Container = styled.div`
  height: 100%;
  display: flex;
  flex-direction: column;
  background: #111823;
`;

const TabHeader = styled.div`
  display: flex;
  border-bottom: 1px solid rgba(87, 100, 122, 0.34);
  background: rgba(13, 19, 29, 0.72);
`;

const Tab = styled.button<{ active: boolean }>`
  flex: 1;
  padding: 0.28rem 0.3rem;
  background: ${props => props.active ? '#f0f6fc' : 'transparent'};
  color: ${props => props.active ? '#0d1117' : '#7d8590'};
  border: none;
  cursor: pointer;
  font-size: 0.66rem;
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
  padding: 0.38rem;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
`;

const DataTable = styled.div`
  flex: 1;
  display: flex;
  flex-direction: column;
  background: #0d1117;
  border-radius: 6px;
  border: 1px solid rgba(87, 100, 122, 0.34);
  overflow: hidden;
`;

const TableHeader = styled.div`
  display: grid;
  padding: 0.36rem 0.5rem;
  background: rgba(17, 24, 35, 0.9);
  border-bottom: 1px solid rgba(87, 100, 122, 0.24);
  font-size: 0.6rem;
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
  padding: 0.32rem 0.5rem;
  border-bottom: 1px solid #21262d;
  font-size: 0.68rem;
  transition: background-color 0.2s;
  &:hover { background: #21262d; }
  &:last-child { border-bottom: none; }
`;

const StatusBadge = styled.span<{ status: string }>`
  padding: 0.15rem 0.35rem;
  border-radius: 8px;
  font-size: 0.6rem;
  font-weight: 700;
  text-transform: uppercase;
  text-align: center;
  background: ${props => {
    switch (props.status) {
      case 'pending': return 'rgba(255, 165, 0, 0.2)';
      case 'active': return 'rgba(88, 166, 255, 0.2)';
      case 'partial': return 'rgba(240, 185, 11, 0.2)';
      case 'filled': return 'rgba(63, 185, 80, 0.2)';
      case 'cancelled': return 'rgba(248, 81, 73, 0.2)';
      default: return 'rgba(125, 133, 144, 0.2)';
    }
  }};
  color: ${props => {
    switch (props.status) {
      case 'pending': return '#ffa500';
      case 'active': return '#58a6ff';
      case 'partial': return '#f0b90b';
      case 'filled': return '#3fb950';
      case 'cancelled': return '#f85149';
      default: return '#7d8590';
    }
  }};
`;

const EmptyState = styled.div`
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  flex: 1;
  color: #7d8590;
  text-align: center;
  min-height: 120px;
  gap: 0.5rem;
`;

const COL_CURRENT = '0.7fr 0.55fr 0.65fr 0.75fr 0.55fr 0.7fr';
const COL_HISTORY = '0.8fr 0.65fr 0.7fr 0.85fr 0.8fr';
const COL_TRADES = '0.9fr 0.6fr 0.9fr 0.8fr';
const COL_ASSETS = '1fr 1fr 1fr 1fr';

const AccountTabs: React.FC = () => {
  const [activeTab, setActiveTab] = useState<'current' | 'history' | 'trades' | 'assets'>('current');
  const { user } = useAuth();
  const { currentOrders, historyOrders, userTrades, assets, isSubscribed } = useUserDataStream();
  const [orders, setOrders] = useState<Order[]>([]);
  const [openOrders, setOpenOrders] = useState<Order[]>([]);
  const [trades, setTrades] = useState<Trade[]>([]);
  const [seededAssets, setSeededAssets] = useState<Asset[]>([]);
  const [loading, setLoading] = useState(false);

  const loadData = async () => {
    if (!user) return;
    setLoading(true);
    try {
      const [all, open, tradeList, assetList] = await Promise.all([
        tradingService.getUserOrders().catch(() => [] as Order[]),
        tradingService.getOpenOrders().catch(() => [] as Order[]),
        tradingService.getUserTrades().catch(() => [] as Trade[]),
        tradingService.getUserAssets().catch(() => [] as Asset[]),
      ]);
      setOrders(all);
      const isOpen = (s: string) => ['pending', 'active', 'partial', 'partiallyfilled'].includes(s.toLowerCase());
      setOpenOrders(open.length ? open : all.filter(o => isOpen(o.status)));
      setTrades(tradeList);
      if (assets.length === 0 && seededAssets.length === 0 && assetList.length > 0) {
        setSeededAssets(assetList);
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { loadData(); }, [user]);
  useEffect(() => { const id = setInterval(loadData, 15000); return () => clearInterval(id); }, [user]);

  useEffect(() => {
    if (currentOrders.length > 0 || historyOrders.length > 0) {
      const map = new Map<string, Order>();
      [...historyOrders, ...currentOrders, ...orders].forEach(o => map.set(o.id, o));
      const merged = Array.from(map.values());
      setOrders(merged.slice(0, 400));
      const isOpen2 = (s: string) => ['pending', 'active', 'partial', 'partiallyfilled'].includes(s.toLowerCase());
      setOpenOrders(merged.filter(o => isOpen2(o.status)));
    }
  }, [currentOrders, historyOrders]);

  useEffect(() => {
    if (userTrades.length > 0) {
      setTrades(prev => {
        const map = new Map<string, Trade>();
        [...userTrades, ...prev].forEach(t => map.set(t.id, t));
        return Array.from(map.values()).slice(0, 200);
      });
    }
  }, [userTrades]);

  const fmt = (v: any, d = 4) => {
    const n = Number(v);
    return Number.isFinite(n) ? n.toFixed(d) : '-';
  };

  const fmtTime = (v: any) => {
    const d = new Date(v);
    return isNaN(d.getTime()) ? '-' : d.toLocaleString('zh-CN', { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hour12: false });
  };

  const normalizeStatus = (s: string): string => {
    const lower = (s || 'pending').toLowerCase();
    if (lower === 'partiallyfilled' || lower === 'partially_filled') return 'partial';
    if (lower === 'filled' || lower === 'fullyfilled') return 'filled';
    if (lower === 'cancelled' || lower === 'rejected') return 'cancelled';
    if (lower === 'active') return 'active';
    return 'pending';
  };

  const renderBadge = (s: string) => {
    const m = normalizeStatus(s);
    return <StatusBadge status={m}>{m}</StatusBadge>;
  };

  const renderCurrentOrders = () => {
    const list = openOrders.length ? openOrders : currentOrders;
    return (
      <DataTable>
        <TableHeader style={{ gridTemplateColumns: COL_CURRENT }}>
          <div>交易对</div><div>方向/类型</div><div>价格</div><div>数量/已成交</div><div>状态</div><div>挂单时间</div>
        </TableHeader>
        <TableBody>
          {list.length > 0 ? list.map(o => (
            <TableRow key={o.id} style={{ gridTemplateColumns: COL_CURRENT }}>
              <div style={{ fontWeight: 600 }}>{o.symbol}</div>
              <div style={{ color: o.side === 'buy' ? '#3fb950' : '#f85149' }}>{o.side}/{o.type}</div>
              <div>{o.price ? fmt(o.price) : '市价'}</div>
              <div>{fmt(o.quantity, 6)} / {o.filledQuantity ? fmt(o.filledQuantity, 6) : '0'}</div>
              <div>{renderBadge(o.status)}</div>
              <div style={{ color: '#7d8590', fontSize: '0.6rem' }}>{fmtTime(o.createdAt)}</div>
            </TableRow>
          )) : (
            <EmptyState>{loading ? '加载中...' : '暂无当前订单'}</EmptyState>
          )}
        </TableBody>
      </DataTable>
    );
  };

  const renderHistoryOrders = () => {
    const isClosed = (s: string) => ['filled', 'cancelled', 'rejected', 'fullyfilled'].includes(s.toLowerCase());
    const list = historyOrders.length ? historyOrders : orders.filter(o => isClosed(o.status));
    return (
      <DataTable>
        <TableHeader style={{ gridTemplateColumns: COL_HISTORY }}>
          <div>交易对</div><div>方向/类型</div><div>均价</div><div>数量/已成交</div><div>状态/时间</div>
        </TableHeader>
        <TableBody>
          {list.length > 0 ? list.map(o => (
            <TableRow key={o.id} style={{ gridTemplateColumns: COL_HISTORY }}>
              <div style={{ fontWeight: 600 }}>{o.symbol}</div>
              <div style={{ color: o.side === 'buy' ? '#3fb950' : '#f85149' }}>{o.side}/{o.type}</div>
              <div>{o.averagePrice ? fmt(o.averagePrice) : (o.price ? fmt(o.price) : '-')}</div>
              <div>{fmt(o.quantity, 6)} / {o.filledQuantity ? fmt(o.filledQuantity, 6) : '0'}</div>
              <div>{renderBadge(o.status)} {fmtTime(o.updatedAt || o.createdAt)}</div>
            </TableRow>
          )) : (
            <EmptyState>{loading ? '加载中...' : '暂无历史订单'}</EmptyState>
          )}
        </TableBody>
      </DataTable>
    );
  };

  const renderTrades = () => (
    <DataTable>
      <TableHeader style={{ gridTemplateColumns: COL_TRADES }}>
        <div>交易对</div><div>方向</div><div>价格/数量</div><div>时间</div>
      </TableHeader>
      <TableBody>
        {trades.length > 0 ? trades.map(t => (
          <TableRow key={t.id} style={{ gridTemplateColumns: COL_TRADES }}>
            <div style={{ fontWeight: 600 }}>{t.symbol}</div>
            <div style={{ color: t.side === 'buy' ? '#3fb950' : '#f85149' }}>{t.side || '-'}</div>
            <div>{fmt(t.price)} / {fmt(t.quantity, 6)}</div>
            <div>{fmtTime(t.executedAt)}</div>
          </TableRow>
        )) : (
          <EmptyState>{loading ? '加载中...' : '暂无成交记录'}</EmptyState>
        )}
      </TableBody>
    </DataTable>
  );

  const renderAssets = () => {
    const list = assets.length > 0 ? assets : seededAssets;
    return (
      <DataTable>
        <TableHeader style={{ gridTemplateColumns: COL_ASSETS }}>
          <div>币种</div><div>可用</div><div>冻结</div><div>总额</div>
        </TableHeader>
        <TableBody>
          {list.length > 0 ? list.map(a => (
            <TableRow key={a.symbol} style={{ gridTemplateColumns: COL_ASSETS }}>
              <div style={{ fontWeight: 700 }}>{a.symbol}</div>
              <div>{fmt(a.available, 8)}</div>
              <div>{fmt(a.frozen, 8)}</div>
              <div>{fmt(a.total, 8)}</div>
            </TableRow>
          )) : (
            <EmptyState>{loading ? '加载中...' : '暂无资产数据'}</EmptyState>
          )}
        </TableBody>
      </DataTable>
    );
  };

  const renderTabContent = () => {
    if (!user) {
      return (
        <EmptyState>
          <Wallet size={24} />
          <div style={{ fontWeight: 600, color: '#f0f6fc' }}>请先登录</div>
          <div style={{ fontSize: '0.8rem' }}>登录后查看交易记录和资产</div>
        </EmptyState>
      );
    }
    switch (activeTab) {
      case 'current': return renderCurrentOrders();
      case 'history': return renderHistoryOrders();
      case 'trades': return renderTrades();
      case 'assets': return renderAssets();
      default: return null;
    }
  };

  const tabs = [
    { key: 'current' as const, icon: Clock, label: `当前委托${isSubscribed ? '' : ' (未订阅)'}` },
    { key: 'history' as const, icon: History, label: '历史委托' },
    { key: 'trades' as const, icon: CheckCircle, label: '成交记录' },
    { key: 'assets' as const, icon: Wallet, label: '我的资产' },
  ];

  return (
    <Container>
      <TabHeader>
        {tabs.map(({ key, icon: Icon, label }) => (
          <Tab key={key} active={activeTab === key} onClick={() => setActiveTab(key)}>
            <Icon size={14} />{label}
          </Tab>
        ))}
      </TabHeader>
      <TabContent>
        {renderTabContent()}
      </TabContent>
    </Container>
  );
};

export default AccountTabs;
