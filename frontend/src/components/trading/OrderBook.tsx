import React, { useState, useEffect } from 'react';
import styled from 'styled-components';
import { useSignalRPriceData } from '../../hooks/useSignalRPriceData';

const Container = styled.div`
  height: 100%;
  display: flex;
  flex-direction: column;
  background: #161b22;
`;

const Header = styled.div`
  padding: 0.5rem 1rem;
  border-bottom: 1px solid #30363d;
  font-weight: 600;
  color: #f0f6fc;
  font-size: 0.8rem;
  background: #21262d;
`;

const HeaderRow = styled.div`
  display: grid;
  grid-template-columns: 1fr 1fr 1fr;
  gap: 0.5rem;
  font-size: 0.75rem;
  color: #7d8590;
  font-weight: 400;
  text-transform: uppercase;
  letter-spacing: 0.5px;
`;

const Content = styled.div`
  flex: 1;
  overflow-y: auto;
`;

const PriceRow = styled.div<{ isBuy?: boolean; isCurrent?: boolean }>`
  display: grid;
  grid-template-columns: 1fr 1fr 1fr;
  gap: 0.5rem;
  padding: 0.25rem 1rem;
  font-size: 0.8rem;
  cursor: pointer;
  transition: background-color 0.1s;
  position: relative;
  
  &:hover {
    background: rgba(255, 255, 255, 0.05);
  }
  
  ${props => props.isCurrent && `
    background: rgba(255, 255, 255, 0.08);
    border-left: 2px solid ${props.isBuy ? '#3fb950' : '#f85149'};
  `}
`;

const PriceCell = styled.div<{ isBuy?: boolean }>`
  color: ${props => props.isBuy ? '#3fb950' : '#f85149'};
  font-weight: 500;
`;

const AmountCell = styled.div`
  color: #f0f6fc;
  text-align: center;
`;

const TotalCell = styled.div`
  color: #7d8590;
  text-align: right;
`;

const DepthBar = styled.div<{ isBuy?: boolean; percentage: number }>`
  position: absolute;
  top: 0;
  bottom: 0;
  ${props => props.isBuy ? 'right' : 'left'}: 0;
  width: ${props => props.percentage}%;
  background: ${props => props.isBuy 
    ? 'rgba(63, 185, 80, 0.1)' 
    : 'rgba(248, 81, 73, 0.1)'};
  z-index: -1;
`;

const CurrentPrice = styled.div`
  padding: 0.5rem 1rem;
  border-top: 1px solid #30363d;
  border-bottom: 1px solid #30363d;
  background: #21262d;
  font-size: 1rem;
  font-weight: 700;
  color: #f0f6fc;
  text-align: center;
  position: relative;
  
  &::before {
    content: '';
    position: absolute;
    top: 0;
    left: 50%;
    transform: translateX(-50%);
    width: 2px;
    height: 100%;
    background: linear-gradient(to bottom, #3fb950, #f85149);
  }
`;

const EmptyState = styled.div`
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
  color: #7d8590;
  font-size: 0.8rem;
`;

interface OrderBookProps {
  symbol: string;
}

const OrderBook: React.FC<OrderBookProps> = ({ symbol }) => {
  // 使用SignalR实时价格数据
  const { priceData, isConnected, error } = useSignalRPriceData([symbol]);
  const [buyOrders, setBuyOrders] = useState<any[]>([]);
  const [sellOrders, setSellOrders] = useState<any[]>([]);
  
  const currentPriceData = priceData[symbol];
  const currentPrice = currentPriceData?.price || 0;

  // TODO: 从后端gRPC服务获取真实的订单簿数据
  // 目前清除了所有模拟数据，等待后端订单簿服务实现

  return (
    <Container>
      <Header>
        <HeaderRow>
          <div>价格(USDT)</div>
          <div>数量</div>
          <div>累计</div>
        </HeaderRow>
      </Header>
      
      <Content>
        {/* 显示当前价格（如果有的话） */}
        <CurrentPrice>
          {currentPrice > 0 ? currentPrice.toFixed(2) : '--'}
          {isConnected && currentPrice > 0 && (
            <span style={{ fontSize: '0.6rem', color: '#00b35f', marginLeft: '8px' }}>●实时</span>
          )}
          {error && (
            <span style={{ fontSize: '0.6rem', color: '#f85149', marginLeft: '8px' }}>●断开</span>
          )}
        </CurrentPrice>
        
        {/* 无数据状态 */}
        <EmptyState>
          {error ? (
            <div>
              <div>订单簿连接失败</div>
              <div style={{ fontSize: '0.7rem', marginTop: '4px', opacity: 0.7 }}>
                SignalR连接错误
              </div>
            </div>
          ) : !isConnected ? (
            <div>
              <div>正在连接订单簿...</div>
              <div style={{ fontSize: '0.7rem', marginTop: '4px', opacity: 0.7 }}>
                等待SignalR连接
              </div>
            </div>
          ) : (
            <div>
              <div>订单簿暂无数据</div>
              <div style={{ fontSize: '0.7rem', marginTop: '4px', opacity: 0.7 }}>
                等待后端订单簿数据推送
              </div>
            </div>
          )}
        </EmptyState>
      </Content>
    </Container>
  );
};

export default OrderBook;