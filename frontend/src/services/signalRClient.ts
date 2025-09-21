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
    if (this.connection?.state === 'Connected') {
      return true;
    }

    if (this.isConnecting) {
      return false;
    }

    this.isConnecting = true;

    try {
      const token = localStorage.getItem('token');
      
      this.connection = new signalR.HubConnectionBuilder()
        .withUrl('https://localhost:50381/tradingHub', {
          accessTokenFactory: () => token || '',
          transport: signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.ServerSentEvents,
          skipNegotiation: false,
        })
        .withHubProtocol(new signalR.JsonHubProtocol())
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Error)
        .build();

      // 设置连接事件
      this.connection.onclose((error?: Error) => {
        this.isConnecting = false;
      });

      this.connection.onreconnecting((error?: Error) => {
        // 重连中
      });

      this.connection.onreconnected((connectionId?: string) => {
        this.reconnectAttempts = 0;
      });

      await this.connection.start();
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

  // 订阅K线数据
  async subscribeKLineData(
    symbol: string,
    interval: string,
    onHistoricalData: (data: KLineData[]) => void,
    onKLineUpdate: (data: KLineData, isNewKLine: boolean) => void,
    onError?: (error: any) => void
  ): Promise<() => void> {
    try {
      if (!this.connection || this.connection.state !== 'Connected') {
        const connected = await this.connect();
        if (!connected || !this.connection) {
          throw new Error('SignalR连接失败');
        }
      }

      // 确保连接存在且已连接
      if (!this.connection || this.connection.state !== 'Connected') {
        throw new Error('SignalR连接状态异常');
      }

      // 设置历史数据接收处理器
      this.connection.off('HistoricalKLineData');
      this.connection.on('HistoricalKLineData', (response: any) => {
        
        if (response.data && Array.isArray(response.data)) {
          const klineData: KLineData[] = response.data.map((item: any) => ({
            timestamp: item.timestamp,
            open: Number(item.open),
            high: Number(item.high),
            low: Number(item.low),
            close: Number(item.close),
            volume: Number(item.volume)
          }));
          
          onHistoricalData(klineData);
        }
      });

      // 设置实时K线更新处理器
      this.connection.off('KLineUpdate');
      this.connection.on('KLineUpdate', (response: any) => {
        
        if (response.kline) {
          const klineData: KLineData = {
            timestamp: response.kline.timestamp,
            open: Number(response.kline.open),
            high: Number(response.kline.high),
            low: Number(response.kline.low),
            close: Number(response.kline.close),
            volume: Number(response.kline.volume)
          };
          
          onKLineUpdate(klineData, response.isNewKLine || false);
        }
      });

      // 发送订阅请求
      await this.connection.invoke('SubscribeKLineData', symbol, interval);

      // 返回取消订阅函数
      return async () => {
        try {
          if (this.connection && this.connection.state === 'Connected') {
            await this.connection.invoke('UnsubscribeKLineData', symbol, interval);
          }
        } catch (error) {
          // 取消订阅失败，忽略错误
        }
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
      if (!this.connection || this.connection.state !== 'Connected') {
        const connected = await this.connect();
        if (!connected || !this.connection) {
          throw new Error('SignalR连接失败');
        }
      }

      // 确保连接存在且已连接
      if (!this.connection || this.connection.state !== 'Connected') {
        throw new Error('SignalR连接状态异常');
      }

      // 设置价格更新处理器
      this.connection.off('PriceData');
      this.connection.off('PriceUpdate');
      
      this.connection.on('PriceData', (response: any) => {
        onPriceUpdate(response);
      });
      
      this.connection.on('PriceUpdate', (response: any) => {
        onPriceUpdate(response);
      });

      // 发送订阅请求
      await this.connection.invoke('SubscribePriceData', symbols);

      // 返回取消订阅函数
      return async () => {
        try {
          if (this.connection && this.connection.state === 'Connected') {
            await this.connection.invoke('UnsubscribePriceData', symbols);
          }
        } catch (error) {
          // 取消订阅失败，忽略错误
        }
      };

    } catch (error) {
      if (onError) onError(error);
      return () => {};
    }
  }

  // 获取连接状态
  isConnected(): boolean {
    return this.connection?.state === 'Connected';
  }

  // 断开连接
  async disconnect(): Promise<void> {
    if (this.connection) {
      try {
        await this.connection.stop();
      } catch (error) {
        // 断开连接失败，忽略错误
      }
    }
  }
}

export const signalRClient = SignalRClient.getInstance();
