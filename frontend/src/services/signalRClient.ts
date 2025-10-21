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
  // log removed

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
        // 注册订阅/取消订阅 ACK 事件的空处理器，避免 SignalR 输出 "No client method ... found" 警告
        [
          'PriceSubscribed','PriceUnsubscribed',
          'KLineSubscribed','KLineUnsubscribed',
          'OrderBookSubscribed','OrderBookUnsubscribed',
          'TickerSubscribed','TickerUnsubscribed',
          'TradesSubscribed','TradesUnsubscribed',
          'UserDataSubscribed','UserDataUnsubscribed',
          'Error'
        ].forEach(attachDebugHandler);

        (window as any).__SR_DEBUG_API = undefined;

        this.connection.onclose((error?: Error) => {
          this.isConnecting = false;
          this.connectPromise = null;
        });
        this.connection.onreconnecting((error?: Error) => { });
        this.connection.onreconnected(async (connectionId?: string) => {
          this.reconnectAttempts = 0;
          try {
            if (this.subscribedPriceSymbols.length) await this.connection!.invoke('SubscribePriceData', this.subscribedPriceSymbols);
            for (const k of this.subscribedKLines) await this.connection!.invoke('SubscribeKLineData', k.symbol, k.interval);
            for (const ob of this.subscribedOrderBooks) await this.connection!.invoke('SubscribeOrderBook', ob.symbol, ob.depth ?? 20);
          } catch (e) { }
        });

        await this.connection.start();
        const cost = Date.now() - startTime;
  // connected
        this.reconnectAttempts = 0;
        this.isConnecting = false;
        const ok = true; resolve(ok);
      } catch (error: any) {
        const msg = error?.message || String(error);
        // 分类错误
  // suppress log

        this.isConnecting = false;
        this.connectPromise = null;
        this.reconnectAttempts++;
        if (this.reconnectAttempts < this.maxReconnectAttempts) {
          // retry notice removed
          setTimeout(() => this.connect(), 3000);
        } else {
          // give up notice removed
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
  // debug removed
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
      // removed debug

      // 设置价格更新处理器：仅使用后端实际推送的 PriceUpdate 事件
      // 清理旧的重复/无效监听（之前额外监听了不存在的 PriceData 事件）
      this.connection.off('PriceUpdate');
      this.connection.on('PriceUpdate', (response: any) => { onPriceUpdate(response); });

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
      
      this.connection.on('OrderBookData', (response: any) => { onOrderBookData(response); });
      this.connection.on('OrderBookUpdate', (response: any) => { onOrderBookData(response); });

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
      
      this.connection.on('LastTradeAndMid', (response: any) => { onTickerData(response); });

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
      const handler = (response: any) => { onTradeUpdate(response); };

      // 添加成交数据接收处理器 (不先off,允许多个订阅者)
      this.connection.on('TradeUpdate', handler);

      // 发送订阅请求
      await this.connection.invoke('SubscribeTrades', symbol);
  // subscribed trades

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
  // subscribe trades failed suppressed
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
