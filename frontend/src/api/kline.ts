import { API_BASE_URL } from './base';

export interface KLineData {
  timestamp: number;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
  symbol: string;
  interval: string;
}

export interface KLineHistoryResponse {
  success: boolean;
  data: KLineData[];
  symbol: string;
  interval: string;
  count: number;
}

export interface KLineLatestResponse {
  success: boolean;
  data: KLineData;
}

export interface KLineSymbolsResponse {
  success: boolean;
  data: string[];
}

export interface KLineIntervalsResponse {
  success: boolean;
  data: string[];
}

/**
 * K线数据API服务
 */
export class KLineApiService {
  /**
   * 获取K线历史数据
   */
  static async getKLineHistory(
    symbol: string,
    interval: string,
    startTime?: number,
    endTime?: number,
    limit: number = 1000
  ): Promise<KLineHistoryResponse> {
    const params = new URLSearchParams({
      symbol,
      interval,
      limit: limit.toString()
    });

    if (startTime) {
      params.append('startTime', startTime.toString());
    }
    if (endTime) {
      params.append('endTime', endTime.toString());
    }

    const response = await fetch(`${API_BASE_URL}/kline/history?${params}`);
    
    if (!response.ok) {
      throw new Error(`获取K线历史数据失败: ${response.statusText}`);
    }

    return await response.json();
  }

  /**
   * 获取最新的K线数据
   */
  static async getLatestKLine(
    symbol: string,
    interval: string
  ): Promise<KLineLatestResponse> {
    const params = new URLSearchParams({
      symbol,
      interval
    });

    const response = await fetch(`${API_BASE_URL}/kline/latest?${params}`);
    
    if (!response.ok) {
      throw new Error(`获取最新K线数据失败: ${response.statusText}`);
    }

    return await response.json();
  }

  /**
   * 获取支持的交易对列表
   */
  static async getSupportedSymbols(): Promise<KLineSymbolsResponse> {
    const response = await fetch(`${API_BASE_URL}/kline/symbols`);
    
    if (!response.ok) {
      throw new Error(`获取交易对列表失败: ${response.statusText}`);
    }

    return await response.json();
  }

  /**
   * 获取支持的时间间隔
   */
  static async getSupportedIntervals(): Promise<KLineIntervalsResponse> {
    const response = await fetch(`${API_BASE_URL}/kline/intervals`);
    
    if (!response.ok) {
      throw new Error(`获取时间间隔列表失败: ${response.statusText}`);
    }

    return await response.json();
  }
}

export default KLineApiService;
