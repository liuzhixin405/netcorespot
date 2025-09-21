import React, { useState, useEffect } from 'react';
import styled from 'styled-components';

const RecentTradesContainer = styled.div`
  height: 100%;
  display: flex;
  flex-direction: column;
  background: #161b22;
`;

const Header = styled.div`
  padding: 0.5rem 1rem;
  border-bottom: 1px solid #30363d;
  background: #21262d;
  font-weight: 600;
  color: #f0f6fc;
  font-size: 0.8rem;
`;

const TradesList = styled.div`
  flex: 1;
  overflow-y: auto;
`;

const TradeRow = styled.div`
  display: grid;
  grid-template-columns: 1fr 1fr 1fr;
  padding: 0.25rem 1rem;
  font-size: 0.7rem;
  border-bottom: 1px solid #21262d;
  transition: background-color 0.1s;

  &:hover {
    background: #21262d;
  }
  
  &:last-child {
    border-bottom: none;
  }
`;

const TimeColumn = styled.div`
  color: #7d8590;
  font-size: 0.7rem;
`;

const PriceColumn = styled.div<{ isBuy?: boolean }>`
  color: ${props => props.isBuy ? '#3fb950' : '#f85149'};
  font-weight: 500;
  text-align: center;
`;

const AmountColumn = styled.div`
  color: #f0f6fc;
  text-align: right;
  font-size: 0.7rem;
`;

const EmptyState = styled.div`
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
  color: #7d8590;
  font-size: 0.8rem;
  text-align: center;
  flex-direction: column;
  gap: 8px;
`;

interface RecentTradesProps {
  symbol: string;
}

interface Trade {
  id: string;
  time: string;
  price: number;
  amount: number;
  isBuy: boolean;
}

const RecentTrades: React.FC<RecentTradesProps> = ({ symbol }) => {
  const [trades, setTrades] = useState<Trade[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // TODO: 从后端gRPC服务获取真实的成交数据
  // 目前清除了所有模拟数据，等待后端成交数据服务实现

  useEffect(() => {
    // 模拟加载状态
    setLoading(false);
    setError('后端成交数据服务未实现');
  }, [symbol]);

  return (
    <RecentTradesContainer>
      <Header>实时成交</Header>
      
      <TradesList>
        {trades.length > 0 ? (
          trades.map((trade, index) => (
            <TradeRow key={trade.id || index}>
              <TimeColumn>{trade.time}</TimeColumn>
              <PriceColumn isBuy={trade.isBuy}>
                {trade.price.toFixed(1)}
              </PriceColumn>
              <AmountColumn>{trade.amount.toFixed(4)}</AmountColumn>
            </TradeRow>
          ))
        ) : (
          <EmptyState>
            {error ? (
              <>
                <div>成交数据连接失败</div>
                <div style={{ fontSize: '0.7rem', opacity: 0.7 }}>
                  需要启动后端gRPC服务
                </div>
              </>
            ) : loading ? (
              <>
                <div>正在加载成交数据...</div>
                <div style={{ fontSize: '0.7rem', opacity: 0.7 }}>
                  等待后端服务响应
                </div>
              </>
            ) : (
              <>
                <div>成交数据暂无</div>
                <div style={{ fontSize: '0.7rem', opacity: 0.7 }}>
                  等待后端成交数据服务实现
                </div>
              </>
            )}
          </EmptyState>
        )}
      </TradesList>
    </RecentTradesContainer>
  );
};

export default RecentTrades;
