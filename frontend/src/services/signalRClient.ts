// 尝试不同的import方式
import * as signalR from '@microsoft/signalr';
import { KLineData } from '../types';

/**
 * SignalR客户端 - 用于实时K线和价格数据推送
 */
export class SignalRClient {
  private static instance: SignalRClient;
  private connection: signalR.HubConnection | null = null;
  private reconnectAttempts = 0;
  private maxReconnectAttempts = 5;
  private isConnecting = false;
  private connectPromise: Promise<boolean> | null = null; // 并发协调

  private subscribedPriceSymbols: string[] = [];
  private subscribedKLines: { symbol: string; interval: string }[] = [];
  private subscribedOrderBooks: { symbol: string; depth: number }[] = [];

  private constructor() {
    // SignalR客户端初始化
  }

  public static getInstance(): SignalRClient {
    if (!SignalRClient.instance) {
      SignalRClient.instance = new SignalRClient();
    }
    return SignalRClient.instance;
  }

  // 建立SignalR连接
  async connect(): Promise<boolean> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) return true;
    // 并发调用等待同一个 promise
    if (this.connectPromise) return this.connectPromise;

    // 如果在“正在连接”阶段返回 false，会导致上层直接抛错；改为统一 promise 等待
    this.isConnecting = true;
    const signalRUrl = process.env.REACT_APP_SIGNALR_URL || 'https://localhost:5001/tradingHub';
    const startTime = Date.now();

    this.connectPromise = new Promise<boolean>(async (resolve) => {
      try {
        const token = localStorage.getItem('token');
        console.log('[SignalR] 尝试连接到:', signalRUrl);

        this.connection = new signalR.HubConnectionBuilder()
          .withUrl(signalRUrl, {
            accessTokenFactory: () => token || '',
          })
          .withAutomaticReconnect({ nextRetryDelayInMilliseconds: ctx => ctx.previousRetryCount < 5 ? 2000 : null })
          .configureLogging(signalR.LogLevel.Information)
          .build();

        const attachDebugHandler = (event: string) => {
          this.connection!.on(event, (...args: any[]) => {
            if ((window as any).__SR_DEBUG) console.log('[SignalR][EVENT]', event, ...args);
          });
        };
        ['PriceSubscribed','KLineSubscribed','OrderBookSubscribed','Error'].forEach(attachDebugHandler);

        (window as any).__SR_DEBUG_API = {
          enable: () => { (window as any).__SR_DEBUG = true; console.log('[SignalR] Debug enabled'); },
            disable: () => { delete (window as any).__SR_DEBUG; console.log('[SignalR] Debug disabled'); },
            conn: () => this.connection,
            resubPrice: async () => { if (this.subscribedPriceSymbols.length) await this.connection!.invoke('SubscribePriceData', this.subscribedPriceSymbols); },
            resubK: async () => { for (const k of this.subscribedKLines) await this.connection!.invoke('SubscribeKLineData', k.symbol, k.interval); },
            resubOB: async () => { for (const o of this.subscribedOrderBooks) await this.connection!.invoke('SubscribeOrderBook', o.symbol, o.depth); }
        };

        this.connection.onclose((error?: Error) => {
          console.warn('[SignalR] WebSocket closed', error);
          this.isConnecting = false;
          this.connectPromise = null;
        });
        this.connection.onreconnecting((error?: Error) => {
          console.warn('[SignalR] Reconnecting WebSocket...', error?.message);
        });
        this.connection.onreconnected(async (connectionId?: string) => {
          console.log('[SignalR] Reconnected, id=', connectionId);
          this.reconnectAttempts = 0;
          try {
            if (this.subscribedPriceSymbols.length) await this.connection!.invoke('SubscribePriceData', this.subscribedPriceSymbols);
            for (const k of this.subscribedKLines) await this.connection!.invoke('SubscribeKLineData', k.symbol, k.interval);
            for (const ob of this.subscribedOrderBooks) await this.connection!.invoke('SubscribeOrderBook', ob.symbol, ob.depth ?? 20);
          } catch (e) { console.warn('[SignalR] Re-subscribe after reconnect failed', e); }
        });

        await this.connection.start();
        const cost = Date.now() - startTime;
        console.log('[SignalR] ✅ 连接成功:', signalRUrl, `(${cost}ms)`);
        console.log('[SignalR] 连接ID:', this.connection.connectionId);
        this.reconnectAttempts = 0;
        this.isConnecting = false;
        const ok = true; resolve(ok);
      } catch (error: any) {
        const msg = error?.message || String(error);
        // 分类错误
        if (msg.includes('404')) console.error('[SignalR] ❌ 连接失败: 404 (检查 Hub 路径 /tradingHub 是否正确, 后端是否启动 HTTPS)');
        else if (msg.includes('Failed to fetch') || msg.includes('TypeError')) console.error('[SignalR] ❌ 网络错误/证书问题: 可能是自签名证书未信任或跨域/CORS 未配置');
        else if (msg.includes('401') || msg.toLowerCase().includes('unauthorized')) console.error('[SignalR] ❌ 未授权: token 可能缺失或无效');
        else console.error('[SignalR] ❌ 连接失败:', msg);

        this.isConnecting = false;
        this.connectPromise = null;
        this.reconnectAttempts++;
        if (this.reconnectAttempts < this.maxReconnectAttempts) {
          console.log(`[SignalR] 将在 3 秒后重试 (${this.reconnectAttempts}/${this.maxReconnectAttempts})...`);
          setTimeout(() => this.connect(), 3000);
        } else {
          console.error('[SignalR] ❌ 达到最大重试次数, 放弃自动重连');
        }
        resolve(false);
      }
    });

    return this.connectPromise;
  }

  // 订阅K线数据（只订阅实时更新）
  async subscribeKLineData(
    symbol: string,
    interval: string,
    onKLineUpdate: (data: KLineData, isNewKLine: boolean) => void,
    onError?: (error: any) => void
  ): Promise<() => void> {
    try {
      if (!this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
        const connected = await this.connect();
        if (!connected || !this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
          throw new Error('SignalR连接失败');
        }
      }

      // 确保连接存在且已连接
      if (!this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
        throw new Error('SignalR连接状态异常');
      }

      // 设置实时K线更新处理器（修复第二参数 isNewKLine 未读取问题）
      this.connection.off('KLineUpdate');
      this.connection.on('KLineUpdate', (payload: any, isNew?: boolean) => {
        if ((window as any).__SR_DEBUG) console.log('[SignalR] KLineUpdate raw=', payload, 'isNewArg=', isNew);
        const response = payload; // 兼容旧变量名
        if (response && response.timestamp) {
          const klineData: KLineData = {
            timestamp: response.timestamp,
            open: Number(response.open),
            high: Number(response.high),
            low: Number(response.low),
            close: Number(response.close),
            volume: Number(response.volume)
          };
          onKLineUpdate(klineData, typeof isNew === 'boolean' ? isNew : !!response.isNewKLine);
        }
      });

      // 发送订阅请求（只订阅实时更新）
      await this.connection.invoke('SubscribeKLineData', symbol, interval);

      // 记录订阅
      if (!this.subscribedKLines.some(k => k.symbol === symbol && k.interval === interval)) {
        this.subscribedKLines.push({ symbol, interval });
      }

      // 返回取消订阅函数
      return async () => {
        try {
          if (this.connection && this.connection.state === signalR.HubConnectionState.Connected) {
            await this.connection.invoke('UnsubscribeKLineData', symbol, interval);
          }
        } catch {
          // 取消订阅失败，忽略错误
        }
        this.subscribedKLines = this.subscribedKLines.filter(k => !(k.symbol === symbol && k.interval === interval));
      };

    } catch (error) {
      if (onError) onError(error);
      return () => {};
    }
  }

  // 订阅价格数据
  async subscribePriceData(
    symbols: string[],
    onPriceUpdate: (priceData: any) => void,
    onError?: (error: any) => void
  ): Promise<() => void> {
    try {
      if (!this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
        const connected = await this.connect();
        if (!connected || !this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
          throw new Error('SignalR连接失败');
        }
      }

      // 确保连接存在且已连接
      if (!this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
        throw new Error('SignalR连接状态异常');
      }

      // 调试: 订阅前输出
      if ((window as any).__SR_DEBUG) {
        console.log('[SignalR] Subscribing price symbols=', symbols);
      }

      // 设置价格更新处理器：仅使用后端实际推送的 PriceUpdate 事件
      // 清理旧的重复/无效监听（之前额外监听了不存在的 PriceData 事件）
      this.connection.off('PriceUpdate');
      this.connection.on('PriceUpdate', (response: any) => {
        if ((window as any).__SR_DEBUG) console.log('[SignalR] PriceUpdate', response);
        onPriceUpdate(response);
      });

      // 发送订阅请求
      await this.connection.invoke('SubscribePriceData', symbols);

      // 合并记录
      symbols.forEach(s => { if (!this.subscribedPriceSymbols.includes(s)) this.subscribedPriceSymbols.push(s); });

      // 返回取消订阅函数
      return async () => {
        try {
          if (this.connection && this.connection.state === signalR.HubConnectionState.Connected) {
            await this.connection.invoke('UnsubscribePriceData', symbols);
          }
        } catch {
          // 取消订阅失败，忽略错误
        }
        this.subscribedPriceSymbols = this.subscribedPriceSymbols.filter(s => !symbols.includes(s));
      };

    } catch (error) {
      if (onError) onError(error);
      return () => {};
    }
  }
  // 订阅订单簿数据
  async subscribeOrderBook(
    symbol: string,
    onOrderBookData: (orderBookData: any) => void,
    onError?: (error: any) => void
  ): Promise<() => void> {
    try {
      if (!this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
        const connected = await this.connect();
        if (!connected || !this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
          throw new Error('SignalR连接失败');
        }
      }

      // 确保连接存在且已连接
      if (!this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
        throw new Error('SignalR连接状态异常');
      }

      // 设置订单簿数据接收处理器
      this.connection.off('OrderBookData');
      this.connection.off('OrderBookUpdate');
      
      this.connection.on('OrderBookData', (response: any) => {
        if ((window as any).__SR_DEBUG) console.log('[SignalR] OrderBookData', response);
        onOrderBookData(response);
      });
      
      this.connection.on('OrderBookUpdate', (response: any) => {
        if ((window as any).__SR_DEBUG) console.log('[SignalR] OrderBookUpdate', response);
        onOrderBookData(response);
      });

      // 发送订阅请求
      await this.connection.invoke('SubscribeOrderBook', symbol, 20);

      // 记录订阅
      if (!this.subscribedOrderBooks.some(o => o.symbol === symbol)) {
        this.subscribedOrderBooks.push({ symbol, depth: 20 });
      }

      // 返回取消订阅函数
      return async () => {
        try {
          if (this.connection && this.connection.state === signalR.HubConnectionState.Connected) {
            await this.connection.invoke('UnsubscribeOrderBook', symbol);
          }
        } catch {
          // 取消订阅失败，忽略错误
        }
        this.subscribedOrderBooks = this.subscribedOrderBooks.filter(o => o.symbol !== symbol);
      };

    } catch (error) {
      if (onError) onError(error);
      return () => {};
    }
  }

  // 订阅实时成交价和中间价
  async subscribeTicker(
    symbol: string,
    onTickerData: (tickerData: any) => void,
    onError?: (error: any) => void
  ): Promise<() => void> {
    try {
      if (!this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
        const connected = await this.connect();
        if (!connected || !this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
          throw new Error('SignalR连接失败');
        }
      }

      // 确保连接存在且已连接
      if (!this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
        throw new Error('SignalR连接状态异常');
      }

      // 设置ticker数据接收处理器
      this.connection.off('LastTradeAndMid');
      
      this.connection.on('LastTradeAndMid', (response: any) => {
        if ((window as any).__SR_DEBUG) console.log('[SignalR] LastTradeAndMid', response);
        onTickerData(response);
      });

      // 发送订阅请求
      await this.connection.invoke('SubscribeTicker', symbol);

      // 返回取消订阅函数
      return async () => {
        try {
          if (this.connection && this.connection.state === signalR.HubConnectionState.Connected) {
            await this.connection.invoke('UnsubscribeTicker', symbol);
          }
        } catch {
          // 取消订阅失败，忽略错误
        }
      };

    } catch (error) {
      if (onError) onError(error);
      return () => {};
    }
  }

  // 订阅实时成交数据
  async subscribeTrades(
    symbol: string,
    onTradeUpdate: (trade: any) => void,
    onError?: (error: any) => void
  ): Promise<() => void> {
    try {
      if (!this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
        const connected = await this.connect();
        if (!connected || !this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
          throw new Error('SignalR连接失败');
        }
      }

      // 确保连接存在且已连接
      if (!this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
        throw new Error('SignalR连接状态异常');
      }

      // 创建命名的处理器,避免被off移除
      const handler = (response: any) => {
        if ((window as any).__SR_DEBUG) console.log('[SignalR] TradeUpdate', response);
        onTradeUpdate(response);
      };

      // 添加成交数据接收处理器 (不先off,允许多个订阅者)
      this.connection.on('TradeUpdate', handler);

      // 发送订阅请求
      await this.connection.invoke('SubscribeTrades', symbol);
      console.log(`[SignalR] Subscribed to trades for ${symbol}`);

      // 返回取消订阅函数
      return async () => {
        try {
          if (this.connection && this.connection.state === signalR.HubConnectionState.Connected) {
            await this.connection.invoke('UnsubscribeTrades', symbol);
            this.connection.off('TradeUpdate', handler); // 只移除这个特定的处理器
          }
        } catch {
          // 取消订阅失败，忽略错误
        }

      };

    } catch (error) {
      console.error('[SignalR] Subscribe trades failed:', error);
      if (onError) onError(error);
      return () => {};
    }
  }

  // 获取连接状态
  isConnected(): boolean {
    return this.connection?.state === signalR.HubConnectionState.Connected;
  }

  // 获取底层连接对象
  getConnection(): signalR.HubConnection | null {
    return this.connection;
  }

  // 断开连接
  async disconnect(): Promise<void> {
    if (this.connection) {
      try {
        await this.connection.stop();
      } catch {
        // 断开连接失败，忽略错误
      }
      // 清空本地订阅记录
      this.subscribedPriceSymbols = [];
      this.subscribedKLines = [];
      this.subscribedOrderBooks = [];
    }
  }
}

export const signalRClient = SignalRClient.getInstance();
