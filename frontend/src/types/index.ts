// 用户相关类型
export interface User {
  id: number;
  username: string;
  email: string;
  createdAt: string;
  lastLoginAt?: string;
}

export interface LoginRequest {
  username: string;
  password: string;
}

export interface RegisterRequest {
  email: string;
  username: string;
  password: string;
}

export interface AuthResponse {
  token: string;
  username: string;
  email: string;
  expiresAt: string;
}

// 加密货币相关类型
export interface CryptoPrice {
  symbol: string;
  price: string;
  change24h: string;
  volume24h: string;
  high24h: string;
  low24h: string;
}

export interface TradingPair {
  symbol: string;
  baseAsset: string;
  quoteAsset: string;
  price: number;
  change24h: number;
  volume24h: number;
}

// 交易相关类型
export interface Order {
  id: number;            // 内部数据库主键
  orderId?: string;      // 业务订单号（可选）
  symbol: string;
  side: 'buy' | 'sell';
  type: 'limit' | 'market';
  quantity: number;
  price?: number;
  filledQuantity?: number;
  remainingQuantity?: number;
  status: 'pending' | 'active' | 'partial' | 'filled' | 'cancelled';
  createdAt: string;
  updatedAt?: string;
  averagePrice?: number; // 预留
}

export interface Trade {
  id: number;
  tradeId?: string;
  symbol: string;
  quantity: number;
  price: number;
  fee?: number;
  feeAsset?: string;
  totalValue?: number;
  executedAt: string;
  side?: 'buy' | 'sell'; // 若后端后续补充
}

export interface Asset {
  symbol: string;
  available: number;
  frozen: number;
  total: number;
  usdtValue?: number; // 可选，本地根据价格计算
}

// K线图相关类型
export interface KLineData {
  timestamp: number;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

export interface TimeFrame {
  value: string;
  label: string;
}

// 订单簿相关类型
export interface OrderBookEntry {
  price: number;
  quantity: number;
  total: number;
}

export interface OrderBook {
  bids: OrderBookEntry[];
  asks: OrderBookEntry[];
  lastUpdateId: number;
}

// API响应类型
export interface ApiResponse<T> {
  success: boolean;
  data: T;
  message?: string;
  error?: string;
}

// 表单相关类型
export interface TradeFormData {
  symbol: string;
  side: 'buy' | 'sell';
  type: 'limit' | 'market';
  quantity: number;
  price?: number;
}

// 组件Props类型
export interface TradingPageProps {}

export interface TradingHeaderProps {
  symbol: string;
  price: number;
  change24h: number;
  volume24h: number;
  onSymbolChange: (symbol: string) => void;
}

export interface KLineChartProps {
  symbol: string;
  timeFrame: string;
  data: KLineData[];
  onTimeFrameChange: (timeFrame: string) => void;
}

export interface OrderBookProps {
  symbol: string;
  bids: OrderBookEntry[];
  asks: OrderBookEntry[];
}

export interface TradeFormProps {
  symbol: string;
  onSubmit: (data: TradeFormData) => void;
  isLoading?: boolean;
}

export interface RecentTradesProps {
  symbol: string;
  trades: Trade[];
}

export interface AccountTabsProps {
  orders: Order[];
  trades: Trade[];
  assets: Asset[];
}
