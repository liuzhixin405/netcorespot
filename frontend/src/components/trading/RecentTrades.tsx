import React, { useState, useEffect } from 'react';
import styled from 'styled-components';
import { tradingApi } from '../../api/trading';
import { signalRClient } from '../../services/signalRClient';

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
  id: number;
  symbol: string;
  price: number;
  quantity: number;
  executedAt: string;
  isBuyerMaker: boolean;
}

const RecentTrades: React.FC<RecentTradesProps> = ({ symbol }) => {
  const [trades, setTrades] = useState<Trade[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const fetchTrades = async () => {
      try {
        setLoading(true);
        setError(null);
        const data = await tradingApi.getRecentTrades(symbol, 50);
        console.log(`[RecentTrades] 获取到 ${symbol} 的成交数据:`, data);
        
        // 确保数据包含所需字段
        const formattedData = data.map((t: any) => ({
          id: t.id,
          symbol: t.symbol,
          price: t.price,
          quantity: t.quantity,
          executedAt: t.executedAt,
          isBuyerMaker: t.isBuyerMaker ?? false
        }));
        console.log(`[RecentTrades] 格式化后的数据:`, formattedData);
        setTrades(formattedData);
      } catch (err) {
        console.error('获取成交数据失败:', err);
        setError('加载失败');
      } finally {
        setLoading(false);
      }
    };

    fetchTrades();
  }, [symbol]);

  // SignalR 实时订阅
  useEffect(() => {
    let unsubscribe: (() => void) | null = null;

    const setupSignalR = async () => {
      try {
        console.log(`[RecentTrades] 开始设置SignalR订阅: ${symbol}`);
        
        // 启用SignalR调试
        if (!(window as any).__SR_DEBUG) {
          (window as any).__SR_DEBUG = true;
        }
        
        // 使用新的 subscribeTrades 方法
        unsubscribe = await signalRClient.subscribeTrades(
          symbol,
          (trade: any) => {
            console.log('[RecentTrades] 🎉 接收到实时成交:', trade);
            if (trade.symbol === symbol) {
              setTrades(prev => {
                // 添加新成交到列表顶部，保持最多50条
                const newTrade: Trade = {
                  id: trade.id,
                  symbol: trade.symbol,
                  price: trade.price,
                  quantity: trade.quantity,
                  executedAt: trade.executedAt,
                  isBuyerMaker: trade.isBuyerMaker
                };
                console.log('[RecentTrades] ✅ 添加成交到列表:', newTrade);
                const newList = [newTrade, ...prev].slice(0, 50);
                console.log('[RecentTrades] 📊 当前成交列表数量:', newList.length);
                return newList;
              });
            } else {
              console.log(`[RecentTrades] ⚠️ 忽略其他交易对的成交: ${trade.symbol} (当前订阅: ${symbol})`);
            }
          },
          (error) => {
            console.error('❌ [RecentTrades] 订阅成交数据失败:', error);
          }
        );
        console.log(`[RecentTrades] ✅ SignalR订阅设置完成: ${symbol}`);
      } catch (err) {
        console.error('❌ [RecentTrades] 设置SignalR订阅失败:', err);
      }
    };

    setupSignalR();

    // 清理函数
    return () => {
      console.log(`[RecentTrades] 🧹 清理SignalR订阅: ${symbol}`);
      if (unsubscribe) {
        unsubscribe();
      }
    };
  }, [symbol]);

  const formatTime = (dateStr: string) => {
    const date = new Date(dateStr);
    return date.toLocaleTimeString('zh-CN', { 
      hour: '2-digit', 
      minute: '2-digit', 
      second: '2-digit' 
    });
  };

  return (
    <RecentTradesContainer>
      <Header>实时成交</Header>
      
      <TradesList>
        {loading && trades.length === 0 ? (
          <EmptyState>
            <div>正在加载成交数据...</div>
          </EmptyState>
        ) : error ? (
          <EmptyState>
            <div>{error}</div>
          </EmptyState>
        ) : trades.length > 0 ? (
          trades.map((trade) => (
            <TradeRow key={`${trade.id}-${trade.executedAt}`}>
              <TimeColumn>{formatTime(trade.executedAt)}</TimeColumn>
              <PriceColumn isBuy={!trade.isBuyerMaker}>
                {trade.price.toFixed(2)}
              </PriceColumn>
              <AmountColumn>{trade.quantity.toFixed(4)}</AmountColumn>
            </TradeRow>
          ))
        ) : (
          <EmptyState>
            <div>暂无成交数据</div>
          </EmptyState>
        )}
      </TradesList>
    </RecentTradesContainer>
  );
};

export default RecentTrades;


