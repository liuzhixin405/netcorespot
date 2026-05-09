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

const BINANCE_BASE = 'https://api.binance.com/api/v3';

const fetchBinanceKlines = async (
  symbol: string,
  interval: string,
  limit: number,
): Promise<KLineData[]> => {
  const binanceInterval = interval; // Binance uses same interval format
  const resp = await fetch(
    `${BINANCE_BASE}/klines?symbol=${symbol}&interval=${binanceInterval}&limit=${limit}`,
  );
  if (!resp.ok) return [];
  const raw: any[][] = await resp.json();
  if (!Array.isArray(raw)) return [];

  return raw.map((item) => ({
    symbol,
    interval,
    timestamp: item[0],         // Open time (ms)
    open: parseFloat(item[1]),
    high: parseFloat(item[2]),
    low: parseFloat(item[3]),
    close: parseFloat(item[4]),
    volume: parseFloat(item[5]),
  }));
};

export class KLineApiService {
  static async getKLineHistory(
    symbol: string,
    interval: string,
    startTime?: number,
    endTime?: number,
    limit: number = 500,
  ): Promise<KLineHistoryResponse> {
    // 先尝试后端 API
    try {
      const params = new URLSearchParams({ symbol, interval, limit: limit.toString() });
      if (startTime) params.append('startTime', startTime.toString());
      if (endTime) params.append('endTime', endTime.toString());

      const response = await fetch(`${API_BASE_URL}/kline/history?${params}`);
      if (response.ok) {
        const json = await response.json();
        if (json.success && json.data?.length > 0) {
          return json;
        }
      }
    } catch {
      // 后端不可用，fall through
    }

    // 兜底：Binance 公开 API
    const data = await fetchBinanceKlines(symbol, interval, limit);
    return {
      success: true,
      data,
      symbol,
      interval,
      count: data.length,
    };
  }

  static async getLatestKLine(
    symbol: string,
    interval: string,
  ): Promise<KLineLatestResponse> {
    try {
      const params = new URLSearchParams({ symbol, interval });
      const response = await fetch(`${API_BASE_URL}/kline/latest?${params}`);
      if (response.ok) {
        const json = await response.json();
        if (json.success) return json;
      }
    } catch {
      // fall through
    }

    // 兜底
    const data = await fetchBinanceKlines(symbol, interval, 1);
    return {
      success: true,
      data: data[0] ?? {
        symbol,
        interval,
        timestamp: Date.now(),
        open: 0, high: 0, low: 0, close: 0, volume: 0,
      },
    };
  }

  static async getSupportedSymbols() {
    return { success: true, data: ['BTCUSDT', 'ETHUSDT', 'SOLUSDT', 'BNBUSDT', 'ADAUSDT', 'DOGEUSDT'] };
  }

  static async getSupportedIntervals() {
    return { success: true, data: ['1m', '5m', '15m', '1h', '4h', '1d', '1w'] };
  }
}

export default KLineApiService;
