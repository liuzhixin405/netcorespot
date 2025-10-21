import { useEffect, useRef, useState, useCallback } from 'react';
import { signalRClient } from '../services/signalRClient';
import { Order, Trade, Asset } from '../types';
import { useAuth } from '../contexts/AuthContext';

interface UseUserDataStreamResult {
  currentOrders: Order[];
  historyOrders: Order[];
  userTrades: Trade[];
  assets: Asset[];
  isSubscribed: boolean;
  reconnect: () => void;
}

// 状态归并策略：
// 1. OrderUpdate 到来：根据状态把订单更新或迁移到 history
// 2. UserTradeUpdate 到来：追加到 userTrades（按 tradeId 去重）
// 3. AssetUpdate 到来：整体替换资产快照
// 4. 初始：仍可通过 REST 加载一次（由外部调用决定，hook 不强制）
export function useUserDataStream(): UseUserDataStreamResult {
  const { user, isAuthenticated } = useAuth();
  const [currentOrders, setCurrentOrders] = useState<Order[]>([]);
  const [historyOrders, setHistoryOrders] = useState<Order[]>([]);
  const [userTrades, setUserTrades] = useState<Trade[]>([]);
  const [assets, setAssets] = useState<Asset[]>([]);
  const [isSubscribed, setIsSubscribed] = useState(false);

  const connectionRef = useRef<any>(null);

  // 合并或插入订单
  const upsertOrder = useCallback((o: Order) => {
    const terminal = ['filled','cancelled'].includes(o.status.toLowerCase());
    if (terminal) {
      // 移到 history
      setHistoryOrders(prev => {
        const existsIndex = prev.findIndex(x => x.id === o.id);
        if (existsIndex >= 0) {
          const copy = [...prev];
          copy[existsIndex] = o;
          return copy;
        }
        return [o, ...prev].slice(0, 200); // 保留最近 200 条
      });
      // 从 current 移除
      setCurrentOrders(prev => prev.filter(x => x.id !== o.id));
    } else {
      setCurrentOrders(prev => {
        const idx = prev.findIndex(x => x.id === o.id);
        if (idx >= 0) {
          const copy = [...prev];
          copy[idx] = o;
          return copy;
        }
        return [o, ...prev].slice(0, 200);
      });
    }
  }, []);

  const addTrade = useCallback((t: Trade) => {
    setUserTrades(prev => {
      if (prev.some(x => x.id === t.id)) return prev; // 去重
      return [t, ...prev].slice(0, 200);
    });
  }, []);

  const handleAssetSnapshot = useCallback((arr: Asset[]) => {
    // 简单替换为最新快照
    setAssets(arr.sort((a,b) => a.symbol.localeCompare(b.symbol)));
  }, []);

  const subscribe = useCallback(async () => {
    if (!isAuthenticated || !user?.id) return;
    const connected = await signalRClient.connect();
    if (!connected) return;
    const conn = signalRClient.getConnection();
    if (!conn) return;

    connectionRef.current = conn;

    // 清理旧监听
    conn.off('OrderUpdate');
    conn.off('UserTradeUpdate');
    conn.off('AssetUpdate');

    // 注册
    conn.on('OrderUpdate', (order: any) => {
      if ((window as any).__SR_DEBUG) console.log('[useUserDataStream] OrderUpdate raw=', order);
      upsertOrder(order as Order);
    });
    conn.on('UserTradeUpdate', (trade: any) => {
      if ((window as any).__SR_DEBUG) console.log('[useUserDataStream] UserTradeUpdate raw=', trade);
      addTrade(trade as Trade);
    });
    conn.on('AssetUpdate', (assetList: any) => {
      if ((window as any).__SR_DEBUG) console.log('[useUserDataStream] AssetUpdate raw=', assetList);
      if (Array.isArray(assetList)) handleAssetSnapshot(assetList as Asset[]);
    });

    try {
      await conn.invoke('SubscribeUserData', user.id);
      setIsSubscribed(true);
    } catch (e) {
      console.error('[useUserDataStream] SubscribeUserData failed', e);
    }
  }, [isAuthenticated, user?.id, upsertOrder, addTrade, handleAssetSnapshot]);

  useEffect(() => {
    subscribe();
    return () => {
      const conn = connectionRef.current;
      if (conn && user?.id) {
        conn.invoke('UnsubscribeUserData', user.id).catch(()=>{});
        conn.off('OrderUpdate');
        conn.off('UserTradeUpdate');
        conn.off('AssetUpdate');
      }
      setIsSubscribed(false);
    };
  }, [subscribe, user?.id]);

  const reconnect = useCallback(() => {
    subscribe();
  }, [subscribe]);

  return { currentOrders, historyOrders, userTrades, assets, isSubscribed, reconnect };
}
