import React, { useState, useEffect, useMemo } from 'react';
import styled from 'styled-components';
import { useSignalRPriceData } from '../../hooks/useSignalRPriceData';
import { useSignalROrderBook, OrderBookLevel } from '../../hooks/useSignalROrderBook';
import { useSignalRTicker } from '../../hooks/useSignalRTicker';

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
  /* 防止内容抖动 */
  min-height: 300px;
  /* 硬件加速优化渲染性能 */
  transform: translateZ(0);
  will-change: contents;
`;

const PriceRow = styled.div<{ isBuy?: boolean; isCurrent?: boolean }>`
  display: grid;
  grid-template-columns: 1fr 1fr 1fr;
  gap: 0.5rem;
  padding: 0.25rem 1rem;
  font-size: 0.8rem;
  cursor: pointer;
  transition: background-color 0.1s ease;
  position: relative;
  /* 防止重新布局 */
  contain: layout style;
  
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
  /* 优化渲染性能 */
  transform: translateZ(0);
  transition: width 0.2s ease-out;
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
  const { priceData, isConnected: priceConnected, error: priceError } = useSignalRPriceData([symbol]);
  // 使用SignalR实时订单簿数据
  const { orderBookData, loading, error: orderBookError, isConnected: orderBookConnected, reconnect } = useSignalROrderBook(symbol);
  // 使用SignalR实时成交价数据
  const { tickerData, isConnected: tickerConnected } = useSignalRTicker(symbol);
  
  const currentPriceData = priceData[symbol];
  const basePrice = currentPriceData?.price || 0;
  
  // 使用useMemo缓存订单数据，避免不必要的重新渲染
  const { buyOrders, sellOrders } = useMemo(() => {
    if (!orderBookData) {
      return { buyOrders: [], sellOrders: [] };
    }
    
    return {
      buyOrders: (orderBookData.bids || []).slice(0, 5),
      sellOrders: (orderBookData.asks || []).slice(0, 5)
    };
  }, [orderBookData]);

  // 获取实时成交价，优先使用ticker数据，然后是价格数据，最后是订单簿中间价
  const currentPrice = useMemo(() => {
    // 1. 优先使用实时成交价
    if (tickerData?.lastPrice && tickerData.lastPrice > 0) {
      return tickerData.lastPrice;
    }
    
    // 2. 使用中间价
    if (tickerData?.midPrice && tickerData.midPrice > 0) {
      return tickerData.midPrice;
    }
    
    // 3. 使用价格数据
    if (basePrice > 0) {
      return basePrice;
    }
    
    // 4. 从订单簿计算中间价
    if (buyOrders.length > 0 && sellOrders.length > 0) {
      return (buyOrders[0].price + sellOrders[0].price) / 2;
    }
    
    // 5. 使用买一或卖一价格
    if (buyOrders.length > 0) return buyOrders[0].price;
    if (sellOrders.length > 0) return sellOrders[0].price;
    
    return 0;
  }, [tickerData, basePrice, buyOrders, sellOrders]);

  // 显示状态管理 - 只有在真正有数据时才显示
  const hasValidData = orderBookData && (buyOrders.length > 0 || sellOrders.length > 0);
  

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
        {/* 显示订单簿数据 - 只有在有有效数据时才渲染 */}
        {hasValidData ? (
          <>
            {/* 卖单（从高到低） */}
            {sellOrders.slice().reverse().map((order: OrderBookLevel, index: number) => {
              const maxSellTotal = Math.max(...sellOrders.map(o => o.total), 1);
              return (
                <PriceRow key={`sell-${order.price}`} isBuy={false}>
                  <DepthBar isBuy={false} percentage={Math.min((order.total / maxSellTotal) * 100, 100)} />
                  <PriceCell isBuy={false}>{order.price.toFixed(2)}</PriceCell>
                  <AmountCell>{order.amount.toFixed(4)}</AmountCell>
                  <TotalCell>{order.total.toFixed(4)}</TotalCell>
                </PriceRow>
              );
            })}
            
            {/* 显示当前价格 - 在买卖订单之间 */}
            <CurrentPrice>
              {currentPrice > 0 ? currentPrice.toFixed(2) : '--'}
            </CurrentPrice>
            
            {/* 买单（从高到低） */}
            {buyOrders.map((order: OrderBookLevel, index: number) => {
              const maxBuyTotal = Math.max(...buyOrders.map(o => o.total), 1);
              return (
                <PriceRow key={`buy-${order.price}`} isBuy={true}>
                  <DepthBar isBuy={true} percentage={Math.min((order.total / maxBuyTotal) * 100, 100)} />
                  <PriceCell isBuy={true}>{order.price.toFixed(2)}</PriceCell>
                  <AmountCell>{order.amount.toFixed(4)}</AmountCell>
                  <TotalCell>{order.total.toFixed(4)}</TotalCell>
                </PriceRow>
              );
            })}
          </>
        ) : (
          /* 空状态 - 静默等待数据加载 */
          <EmptyState>
            {loading ? (
              <div style={{ color: '#7d8590', fontSize: '0.8rem' }}>
                加载订单簿...
              </div>
            ) : (
              <div style={{ opacity: 0.6, fontSize: '0.8rem' }}>
                {orderBookError ? '连接失败' : '等待数据...'}
              </div>
            )}
          </EmptyState>
        )}
      </Content>
    </Container>
  );
};

export default OrderBook;