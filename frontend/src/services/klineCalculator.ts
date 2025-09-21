import { KLineData } from '../types';

export class KLineCalculator {
  // 从1分钟K线计算其他时间段的K线
  static calculateKLineFromMinutes(
    minuteKLines: KLineData[],
    targetInterval: string
  ): KLineData[] {
    if (!minuteKLines || minuteKLines.length === 0) {
      return [];
    }

    const intervalMs = this.getIntervalMs(targetInterval);
    
    // 如果目标就是1分钟，直接返回
    if (targetInterval === '1m') {
      return minuteKLines;
    }

    // 按时间段分组并计算OHLCV
    const groupedKLines = this.groupByTimeInterval(minuteKLines, intervalMs);
    const calculatedKLines = this.calculateOHLCV(groupedKLines);
    
    return calculatedKLines.sort((a, b) => a.timestamp - b.timestamp);
  }

  // 按时间间隔分组1分钟K线
  private static groupByTimeInterval(
    minuteKLines: KLineData[],
    intervalMs: number
  ): { [key: number]: KLineData[] } {
    const groups: { [key: number]: KLineData[] } = {};

    minuteKLines.forEach(kline => {
      // 计算该分钟K线属于哪个时间段
      const periodStart = Math.floor(kline.timestamp / intervalMs) * intervalMs;
      
      if (!groups[periodStart]) {
        groups[periodStart] = [];
      }
      groups[periodStart].push(kline);
    });

    return groups;
  }

  // 计算每个时间段的OHLCV
  private static calculateOHLCV(
    groupedKLines: { [key: number]: KLineData[] }
  ): KLineData[] {
    const result: KLineData[] = [];

    Object.entries(groupedKLines).forEach(([periodStartStr, klines]) => {
      const periodStart = parseInt(periodStartStr);
      
      if (klines.length === 0) return;
      
      // 按时间排序
      const sortedKLines = klines.sort((a, b) => a.timestamp - b.timestamp);
      
      // 计算OHLCV
      const open = sortedKLines[0].open; // 第一根K线的开盘价
      const close = sortedKLines[sortedKLines.length - 1].close; // 最后一根K线的收盘价
      const high = Math.max(...sortedKLines.map(k => k.high)); // 最高价
      const low = Math.min(...sortedKLines.map(k => k.low)); // 最低价
      const volume = sortedKLines.reduce((sum, k) => sum + k.volume, 0); // 成交量累加

      result.push({
        timestamp: periodStart,
        open,
        high,
        low,
        close,
        volume
      });
    });

    return result;
  }

  // 获取时间间隔毫秒数
  private static getIntervalMs(interval: string): number {
    switch (interval) {
      case '1m': return 60 * 1000;
      case '5m': return 5 * 60 * 1000;
      case '15m': return 15 * 60 * 1000;
      case '1h': return 60 * 60 * 1000;
      case '4h': return 4 * 60 * 60 * 1000;
      case '1d': return 24 * 60 * 60 * 1000;
      default: return 60 * 1000;
    }
  }

  // 实时更新计算后的K线数据
  static updateCalculatedKLine(
    existingKLines: KLineData[],
    newMinuteKLine: KLineData,
    targetInterval: string
  ): KLineData[] {
    if (targetInterval === '1m') {
      // 1分钟数据直接更新
      const existingIndex = existingKLines.findIndex(
        k => k.timestamp === newMinuteKLine.timestamp
      );
      
      if (existingIndex >= 0) {
        const updated = [...existingKLines];
        updated[existingIndex] = newMinuteKLine;
        return updated;
      } else {
        return [...existingKLines, newMinuteKLine].sort((a, b) => a.timestamp - b.timestamp);
      }
    }

    // 其他时间段需要重新计算
    const intervalMs = this.getIntervalMs(targetInterval);
    const periodStart = Math.floor(newMinuteKLine.timestamp / intervalMs) * intervalMs;
    
    // 找到需要更新的时间段
    const existingIndex = existingKLines.findIndex(k => k.timestamp === periodStart);
    
    if (existingIndex >= 0) {
      // 更新现有时间段的K线
      // 这里需要重新计算该时间段内所有1分钟K线的OHLCV
      // 简化实现：只更新收盘价和成交量
      const updated = [...existingKLines];
      updated[existingIndex] = {
        ...updated[existingIndex],
        close: newMinuteKLine.close,
        high: Math.max(updated[existingIndex].high, newMinuteKLine.high),
        low: Math.min(updated[existingIndex].low, newMinuteKLine.low),
        volume: updated[existingIndex].volume + newMinuteKLine.volume
      };
      return updated;
    } else {
      // 新的时间段，添加新K线
      const newKLine: KLineData = {
        timestamp: periodStart,
        open: newMinuteKLine.open,
        high: newMinuteKLine.high,
        low: newMinuteKLine.low,
        close: newMinuteKLine.close,
        volume: newMinuteKLine.volume
      };
      
      return [...existingKLines, newKLine].sort((a, b) => a.timestamp - b.timestamp);
    }
  }
}
