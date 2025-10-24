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

interface TradingPairMeta {
  symbol: string;
  quantityPrecision: number;
  minQuantity: number;
  pricePrecision: number;
}

const RecentTrades: React.FC<RecentTradesProps> = ({ symbol }) => {
  const [trades, setTrades] = useState<Trade[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [pairMeta, setPairMeta] = useState<TradingPairMeta | null>(null);
  // 是否隐藏微尘成交（默认隐藏）
  const [hideDust, setHideDust] = useState(true);
  // 统计被隐藏的微尘条目数量（用于提示）
  const [hiddenDustCount, setHiddenDustCount] = useState(0);

  useEffect(() => {
    // ✅ 切换交易对时立即清空旧数据
    setTrades([]);
    setHiddenDustCount(0);
    
    const fetchTrades = async () => {
      try {
        setLoading(true);
        setError(null);
        // 并行获取交易对信息与最近成交
        const [pairResp, tradesResp] = await Promise.all([
          // 复用 tradingApi （假设存在 getTradingPairInfo 方法；若不存在可后续实现）
          (tradingApi as any).getTradingPairInfo ? (tradingApi as any).getTradingPairInfo(symbol) : Promise.resolve(null),
          tradingApi.getRecentTrades(symbol, 50)
        ]);
        const data = tradesResp;
        if (pairResp && pairResp.success && pairResp.data) {
          setPairMeta({
            symbol: pairResp.data.symbol,
            quantityPrecision: pairResp.data.quantityPrecision ?? 4,
            minQuantity: pairResp.data.minQuantity ?? 0.0001,
            pricePrecision: pairResp.data.pricePrecision ?? 2
          });
        } else {
          // 回退：根据 symbol 粗略推断
            let qp = 4; let minQ = 0.0001; let pp = 2;
            if (symbol.startsWith('BTC')) { qp = 5; minQ = 0.00001; }
            else if (symbol.startsWith('ETH')) { qp = 3; minQ = 0.001; }
            else if (symbol.startsWith('SOL')) { qp = 2; minQ = 0.01; pp = 3; }
            setPairMeta({ symbol, quantityPrecision: qp, minQuantity: minQ, pricePrecision: pp });
        }
        
        // 确保数据包含所需字段
        const formattedData = data.map((t: any) => ({
          id: t.id,
          symbol: t.symbol,
          price: t.price,
          quantity: t.quantity,
          executedAt: t.executedAt,
          isBuyerMaker: t.isBuyerMaker ?? false
        }));
        setTrades(formattedData);
      } catch (err) {
  // fetch error suppressed
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
    let currentSymbol = symbol; // ✅ 捕获当前 symbol

    const setupSignalR = async () => {
      try {
        // 启用SignalR调试
        if (!(window as any).__SR_DEBUG) {
          (window as any).__SR_DEBUG = true;
        }
        
        // 使用新的 subscribeTrades 方法
        unsubscribe = await signalRClient.subscribeTrades(
          symbol,
          (trade: any) => {
            // ✅ 使用捕获的 currentSymbol 而不是闭包的 symbol
            if (trade.symbol === currentSymbol) {
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
                const newList = [newTrade, ...prev].slice(0, 50);
                return newList;
              });
            }
          },
          (error) => {
            console.error('[RecentTrades] 订阅失败:', error);
          }
        );
      } catch (err) {
        console.error('[RecentTrades] setupSignalR 失败:', err);
      }
    };

    setupSignalR();

    // 清理函数
    return () => {
      if (unsubscribe) {
        unsubscribe();
      }
      // ✅ 标记当前 symbol 失效
      currentSymbol = '';
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

  const formatQty = (q: number) => {
    if (!pairMeta) return q.toFixed(4);
    const precision = pairMeta.quantityPrecision ?? 4;
    const minVisible = 1 / Math.pow(10, precision); // 最小刻度
    if (q === 0) return '0';
    if (q > 0 && q < minVisible) {
      // 显示为 <最小刻度 并在 title 中给全量
      return `<${minVisible.toFixed(precision)}`;
    }
    return q.toFixed(precision);
  };

  const formatPrice = (p: number) => {
    if (!pairMeta) return p.toFixed(2);
    return p.toFixed(pairMeta.pricePrecision ?? 2);
  };

  // 计算 dust 阈值（minQuantity * 0.1，可根据需要调整或做成配置）
  const dustThreshold = pairMeta ? pairMeta.minQuantity * 0.1 : 0;

  // 判断是否为微尘成交
  const isDust = (t: Trade) => {
    if (!pairMeta) return false; // 尚未有元数据不判定为 dust
    return t.quantity > 0 && t.quantity < dustThreshold;
  };

  // 根据 hideDust 过滤
  const displayedTrades = trades.filter(t => {
    const dust = isDust(t);
    return hideDust ? !dust : true;
  });

  // 更新隐藏条数（当 trades / hideDust 改变时）
  useEffect(() => {
    if (!hideDust) {
      setHiddenDustCount(0);
      return;
    }
    if (!pairMeta) {
      setHiddenDustCount(0);
      return;
    }
    const cnt = trades.reduce((acc, t) => acc + (isDust(t) ? 1 : 0), 0);
    setHiddenDustCount(cnt);
  }, [trades, hideDust, pairMeta]);

  const ToggleBar = (
    <div style={{
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      padding: '0.4rem 0.75rem',
      borderBottom: '1px solid #21262d',
      background: '#1b2026'
    }}>
      <div style={{ display: 'flex', flexDirection: 'column', fontSize: '0.65rem', color: '#7d8590' }}>
        <span>
          阈值: {dustThreshold ? dustThreshold.toPrecision(2) : '-'} ({pairMeta?.minQuantity ? 'minQty x 0.1' : '推断中'})
        </span>
        {hideDust && hiddenDustCount > 0 && (
          <span style={{ color: '#9e6bff' }}>已隐藏 {hiddenDustCount} 条微尘</span>
        )}
      </div>
      <button
        style={{
          background: hideDust ? '#30363d' : '#238636',
            color: '#f0f6fc',
            border: '1px solid #30363d',
            borderRadius: 4,
            cursor: 'pointer',
            fontSize: '0.65rem',
            padding: '0.25rem 0.5rem',
            lineHeight: 1
        }}
        onClick={() => setHideDust(v => !v)}
        title={hideDust ? '显示所有包含极小数量的成交' : '隐藏数量极小(微尘)的成交'}
      >
        {hideDust ? '显示微尘' : '隐藏微尘'}
      </button>
    </div>
  );

  return (
    <RecentTradesContainer>
      <Header>实时成交</Header>
      
      {ToggleBar}
      <TradesList>
        {loading && trades.length === 0 ? (
          <EmptyState>
            <div>正在加载成交数据...</div>
          </EmptyState>
        ) : error ? (
          <EmptyState>
            <div>{error}</div>
          </EmptyState>
        ) : displayedTrades.length > 0 ? (
          displayedTrades.map((trade) => {
            const qtyDisplay = formatQty(trade.quantity);
            const priceDisplay = formatPrice(trade.price);
            const dust = isDust(trade);
            return (
              <TradeRow
                key={`${trade.id}-${trade.executedAt}`}
                title={`数量: ${trade.quantity} 价格: ${trade.price}${dust ? ' (微尘)' : ''}`}
                style={dust ? { opacity: 0.6 } : undefined}
              >
                <TimeColumn>{formatTime(trade.executedAt)}</TimeColumn>
                <PriceColumn isBuy={!trade.isBuyerMaker}>
                  {priceDisplay}
                </PriceColumn>
                <AmountColumn>
                  {qtyDisplay}{dust && !hideDust ? '*' : ''}
                </AmountColumn>
              </TradeRow>
            );
          })
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


