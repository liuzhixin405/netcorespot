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
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      return true;
    }

    if (this.isConnecting) {
      return false;
    }

    this.isConnecting = true;

    try {
      const token = localStorage.getItem('token');
      
      const signalRUrl = process.env.REACT_APP_SIGNALR_URL || 'https://localhost:5001/tradingHub';
      
      this.connection = new signalR.HubConnectionBuilder()
        .withUrl(signalRUrl, {
          accessTokenFactory: () => token || '',
          transport: signalR.HttpTransportType.WebSockets, // 强制只用 WebSocket
          skipNegotiation: true // 跳过协商
        })
        .withHubProtocol(new signalR.JsonHubProtocol())
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Information)
        .build();

      // 调试: 统一事件调试输出
      const attachDebugHandler = (event: string) => {
        this.connection!.on(event, (...args: any[]) => {
          if ((window as any).__SR_DEBUG) {
            console.log('[SignalR][EVENT]', event, ...args);
          }
        });
      };
      ['PriceSubscribed','KLineSubscribed','OrderBookSubscribed','Error'].forEach(attachDebugHandler);

      // 将简单调试 API 暴露到 window
      (window as any).__SR_DEBUG_API = {
        enable: () => { (window as any).__SR_DEBUG = true; console.log('[SignalR] Debug enabled'); },
        disable: () => { delete (window as any).__SR_DEBUG; console.log('[SignalR] Debug disabled'); },
        conn: () => this.connection,
        resubPrice: async () => { if (this.subscribedPriceSymbols.length) await this.connection!.invoke('SubscribePriceData', this.subscribedPriceSymbols); },
        resubK: async () => { for (const k of this.subscribedKLines) await this.connection!.invoke('SubscribeKLineData', k.symbol, k.interval); },
        resubOB: async () => { for (const o of this.subscribedOrderBooks) await this.connection!.invoke('SubscribeOrderBook', o.symbol, o.depth); }
      };

      // 设置连接事件
      this.connection.onclose((error?: Error) => {
        console.warn('[SignalR] WebSocket closed', error);
        this.isConnecting = false;
      });

      this.connection.onreconnecting((error?: Error) => {
        console.warn('[SignalR] Reconnecting WebSocket...', error);
      });

      this.connection.onreconnected(async (connectionId?: string) => {
        console.log('[SignalR] Reconnected via WebSocket, id=', connectionId);
        this.reconnectAttempts = 0;
        try {
          // 重连后重新订阅
          if (this.subscribedPriceSymbols.length) {
            await this.connection!.invoke('SubscribePriceData', this.subscribedPriceSymbols);
          }
          for (const k of this.subscribedKLines) {
            await this.connection!.invoke('SubscribeKLineData', k.symbol, k.interval);
          }
          for (const ob of this.subscribedOrderBooks) {
            await this.connection!.invoke('SubscribeOrderBook', ob.symbol, ob.depth ?? 20);
          }
        } catch (e) {
          console.warn('[SignalR] Re-subscribe after reconnect failed', e);
        }
      });

      await this.connection.start();
      console.log('[SignalR] Connected via WebSocket');
      this.reconnectAttempts = 0;
      this.isConnecting = false;
      
      return true;
    } catch (error) {
      this.isConnecting = false;
      this.reconnectAttempts++;
      
      if (this.reconnectAttempts < this.maxReconnectAttempts) {
        setTimeout(() => this.connect(), 3000);
      }
      
      return false;
    }
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
        if (!connected || !this.connection) {
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
        if (!connected || !this.connection) {
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

      // 设置价格更新处理器
      this.connection.off('PriceData');
      this.connection.off('PriceUpdate');
      
      this.connection.on('PriceData', (response: any) => {
        onPriceUpdate(response);
      });
      
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
        if (!connected || !this.connection) {
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
        if (!connected || !this.connection) {
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
        if (!connected || !this.connection) {
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
