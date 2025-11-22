// 用户相关类型
export interface User {
  id: string; // 对应后端long类型,JSON序列化为字符串
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
  expiresAt: string;
  user: {
    id: string | number; // 兼容后端可能返回number或string
    username: string;
    email: string;
    createdAt: string;
    lastLoginAt?: string;
  };
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
  id?: string; // 交易对ID(long类型)
  symbol: string;
  baseAsset: string;
  quoteAsset: string;
  price: number;
  change24h: number;
  volume24h: number;
  minQuantity?: number; // 最小交易数量
  maxQuantity?: number; // 最大交易数量
  pricePrecision?: number; // 价格精度
  quantityPrecision?: number; // 数量精度
  isActive?: boolean; // 是否激活
}

// 交易相关类型
export interface Order {
  id: string;            // 内部数据库主键(long类型)
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
  averagePrice?: number;
  tradingPairId?: string; // 交易对ID(long类型)
  userId?: string; // 用户ID(long类型)
}

export interface Trade {
  id: string; // 对应后端long类型
  tradeId?: string;
  symbol: string;
  quantity: number;
  price: number;
  fee?: number;
  feeAsset?: string;
  totalValue?: number;
  executedAt: string;
  side?: 'buy' | 'sell';
  buyOrderId?: string; // 买方订单ID(long类型)
  sellOrderId?: string; // 卖方订单ID(long类型)
  buyerId?: string; // 买方用户ID(long类型)
  sellerId?: string; // 卖方用户ID(long类型)
}

export interface Asset {
  id?: string; // 资产ID(long类型)
  userId?: string; // 用户ID(long类型)
  symbol: string;
  available: number;
  frozen: number;
  total: number;
  usdtValue?: number; // 可选，本地根据价格计算
  minReserve?: number; // 最小保留余额
  targetBalance?: number; // 目标余额
  autoRefillEnabled?: boolean; // 是否启用自动充值
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

export interface ApiResponseDto<T> {
  success: boolean;
  data: T | null;
  message?: string;
  error?: string;
  errorCode?: string;
  timestamp?: string;
  requestId?: string;
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
