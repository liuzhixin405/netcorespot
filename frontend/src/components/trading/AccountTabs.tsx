import React, { useState } from 'react';
import styled from 'styled-components';
import { Clock, History, CheckCircle, Wallet } from 'lucide-react';
import { useAuth } from '../../contexts/AuthContext';

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

  // 所有模拟数据已清除，等待后端API服务实现
  const orders: any[] = [];
  const trades: any[] = [];
  const assets: any[] = [];

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
      case 'current':
        return (
          <AuthenticatedContent>
            <DataTable>
              <TableHeader>
                <div>交易对</div>
                <div>类型</div>
                <div>价格</div>
                <div>状态</div>
              </TableHeader>
              <TableBody>
                {orders.length > 0 ? (
                  orders.map(order => (
                    <TableRow key={order.id}>
                      <div>{order.symbol}</div>
                      <div style={{ color: order.type === '买入' ? '#3fb950' : '#f85149' }}>{order.type}</div>
                      <div>{order.price}</div>
                      <div><StatusBadge status={order.status}>{order.status}</StatusBadge></div>
                    </TableRow>
                  ))
                ) : (
                  <TableRow>
                    <div style={{ gridColumn: '1 / -1', textAlign: 'center', color: '#7d8590', padding: '20px' }}>
                      暂无当前订单数据
                    </div>
                  </TableRow>
                )}
              </TableBody>
            </DataTable>
          </AuthenticatedContent>
        );
      
      case 'history':
        return (
          <AuthenticatedContent>
            <DataTable>
              <TableHeader>
                <div>交易对</div>
                <div>类型</div>
                <div>价格</div>
                <div>时间</div>
              </TableHeader>
              <TableBody>
                {orders.length > 0 ? (
                  orders.map(order => (
                    <TableRow key={order.id}>
                      <div>{order.symbol}</div>
                      <div style={{ color: order.type === '买入' ? '#3fb950' : '#f85149' }}>{order.type}</div>
                      <div>{order.price}</div>
                      <div>{order.time}</div>
                    </TableRow>
                  ))
                ) : (
                  <TableRow>
                    <div style={{ gridColumn: '1 / -1', textAlign: 'center', color: '#7d8590', padding: '20px' }}>
                      暂无历史订单数据
                    </div>
                  </TableRow>
                )}
              </TableBody>
            </DataTable>
          </AuthenticatedContent>
        );
      
      case 'trades':
        return (
          <AuthenticatedContent>
            <DataTable>
              <TableHeader>
                <div>交易对</div>
                <div>类型</div>
                <div>成交价</div>
                <div>手续费</div>
              </TableHeader>
              <TableBody>
                {trades.length > 0 ? (
                  trades.map(trade => (
                    <TableRow key={trade.id}>
                      <div>{trade.symbol}</div>
                      <div style={{ color: trade.type === '买入' ? '#3fb950' : '#f85149' }}>{trade.type}</div>
                      <div>{trade.price}</div>
                      <div>{trade.fee}</div>
                    </TableRow>
                  ))
                ) : (
                  <TableRow>
                    <div style={{ gridColumn: '1 / -1', textAlign: 'center', color: '#7d8590', padding: '20px' }}>
                      暂无成交记录数据
                    </div>
                  </TableRow>
                )}
              </TableBody>
            </DataTable>
          </AuthenticatedContent>
        );
      
      case 'assets':
        return (
          <AuthenticatedContent>
            <DataTable>
              <TableHeader>
                <div>币种</div>
                <div>可用余额</div>
                <div>冻结</div>
                <div>估值(USDT)</div>
              </TableHeader>
              <TableBody>
                {assets.length > 0 ? (
                  assets.map(asset => (
                    <TableRow key={asset.symbol}>
                      <div style={{ fontWeight: 'bold', color: '#f0f6fc' }}>{asset.symbol}</div>
                      <div>{asset.balance}</div>
                      <div>{asset.frozen}</div>
                      <div>{asset.value}</div>
                    </TableRow>
                  ))
                ) : (
                  <TableRow>
                    <div style={{ gridColumn: '1 / -1', textAlign: 'center', color: '#7d8590', padding: '20px' }}>
                      暂无资产数据
                    </div>
                  </TableRow>
                )}
              </TableBody>
            </DataTable>
          </AuthenticatedContent>
        );
      
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