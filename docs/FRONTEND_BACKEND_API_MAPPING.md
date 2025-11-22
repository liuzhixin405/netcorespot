# 前后端接口类型对应说明

## 概述
本文档说明前后端ID类型的统一方案和接口对应关系。

## ID类型统一方案

### 后端 (C# .NET)
- **所有实体ID**: 统一使用 `long` 类型 (Int64, 8字节)
- **JSON序列化**: 通过配置将long序列化为字符串以避免JavaScript精度丢失

### 前端 (TypeScript)
- **所有ID字段**: 统一使用 `string` 类型
- **原因**: JavaScript的Number类型(IEEE 754 double)只能安全表示 ±2^53-1 的整数，而C# long可达 ±2^63-1

## 类型映射表

| 后端类型 (C#) | 前端类型 (TypeScript) | JSON传输 | 说明 |
|--------------|---------------------|---------|------|
| `long` | `string` | `"123456789012345"` | 所有实体ID |
| `int` | `number` | `123` | 配置参数、枚举值 |
| `decimal` | `number` | `123.45` | 价格、数量 |
| `DateTime` | `string` | `"2025-11-22T10:30:00Z"` | ISO 8601格式 |
| `bool` | `boolean` | `true/false` | 布尔值 |

## 主要实体类型对应

### 1. User (用户)

**后端 (C#)**:
```csharp
public class UserDto
{
    public long Id { get; set; }                    // long
    public string Username { get; set; }
    public string? Email { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

**前端 (TypeScript)**:
```typescript
export interface User {
  id: string;              // 对应后端long
  username: string;
  email: string;
  createdAt: string;       // ISO 8601字符串
  lastLoginAt?: string;
}
```

### 2. Order (订单)

**后端 (C#)**:
```csharp
public class OrderDto
{
    public long Id { get; set; }                    // 订单内部ID
    public string OrderId { get; set; }             // 业务订单号
    public long? UserId { get; set; }               // 用户ID
    public long TradingPairId { get; set; }         // 交易对ID
    public string Symbol { get; set; }
    public OrderSide Side { get; set; }             // 枚举: 0=Buy, 1=Sell
    public OrderType Type { get; set; }             // 枚举: 0=Limit, 1=Market
    public decimal Quantity { get; set; }
    public decimal? Price { get; set; }
    public decimal FilledQuantity { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

**前端 (TypeScript)**:
```typescript
export interface Order {
  id: string;                    // 对应后端long
  orderId?: string;              // 业务订单号
  userId?: string;               // 对应后端long
  tradingPairId?: string;        // 对应后端long
  symbol: string;
  side: 'buy' | 'sell';         // 字符串枚举
  type: 'limit' | 'market';     // 字符串枚举
  quantity: number;
  price?: number;
  filledQuantity?: number;
  status: 'pending' | 'active' | 'partial' | 'filled' | 'cancelled';
  createdAt: string;
}
```

### 3. Trade (交易记录)

**后端 (C#)**:
```csharp
public class TradeDto
{
    public long Id { get; set; }
    public string TradeId { get; set; }
    public long BuyOrderId { get; set; }
    public long SellOrderId { get; set; }
    public long BuyerId { get; set; }
    public long SellerId { get; set; }
    public string Symbol { get; set; }
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public decimal Fee { get; set; }
    public DateTime ExecutedAt { get; set; }
}
```

**前端 (TypeScript)**:
```typescript
export interface Trade {
  id: string;                // 对应后端long
  tradeId?: string;
  buyOrderId?: string;       // 对应后端long
  sellOrderId?: string;      // 对应后端long
  buyerId?: string;          // 对应后端long
  sellerId?: string;         // 对应后端long
  symbol: string;
  price: number;
  quantity: number;
  fee?: number;
  executedAt: string;
}
```

### 4. Asset (资产)

**后端 (C#)**:
```csharp
public class AssetDto
{
    public long Id { get; set; }
    public long? UserId { get; set; }
    public string Symbol { get; set; }
    public decimal Available { get; set; }
    public decimal Frozen { get; set; }
    public decimal Total { get; set; }
    public decimal MinReserve { get; set; }
    public decimal TargetBalance { get; set; }
    public bool AutoRefillEnabled { get; set; }
}
```

**前端 (TypeScript)**:
```typescript
export interface Asset {
  id?: string;               // 对应后端long
  userId?: string;           // 对应后端long
  symbol: string;
  available: number;
  frozen: number;
  total: number;
  minReserve?: number;
  targetBalance?: number;
  autoRefillEnabled?: boolean;
}
```

### 5. TradingPair (交易对)

**后端 (C#)**:
```csharp
public class TradingPairDto
{
    public long Id { get; set; }
    public string Symbol { get; set; }
    public string BaseAsset { get; set; }
    public string QuoteAsset { get; set; }
    public decimal MinQuantity { get; set; }
    public decimal MaxQuantity { get; set; }
    public int PricePrecision { get; set; }
    public int QuantityPrecision { get; set; }
    public bool IsActive { get; set; }
}
```

**前端 (TypeScript)**:
```typescript
export interface TradingPair {
  id?: string;               // 对应后端long
  symbol: string;
  baseAsset: string;
  quoteAsset: string;
  minQuantity?: number;
  maxQuantity?: number;
  pricePrecision?: number;
  quantityPrecision?: number;
  isActive?: boolean;
}
```

## API接口路径

### 后端控制器 (TradingController)
- Base Path: `/api/trading`

### 前端API调用 (TradingApi)
- Base URL: `http://localhost:5000/api`
- Trading Base: `/trading`

### 接口对应关系

| 功能 | 后端路由 | 前端方法 | 参数类型 |
|-----|---------|---------|---------|
| 获取交易对列表 | `GET /api/trading/pairs` | `getTradingPairs()` | - |
| 获取K线数据 | `GET /api/trading/klines/{symbol}` | `getKLineData(symbol, timeFrame, limit)` | string, string, number |
| 获取订单簿 | `GET /api/trading/orderbook/{symbol}` | `getOrderBook(symbol, limit)` | string, number |
| 获取市场成交 | `GET /api/trading/market/trades/{symbol}` | `getRecentTrades(symbol, limit)` | string, number |
| 提交订单 | `POST /api/trading/orders` | `submitOrder(orderData)` | TradeFormData |
| 获取开放订单 | `GET /api/trading/orders/open` | `getOpenOrders(symbol?)` | string? |
| 获取所有订单 | `GET /api/trading/orders` | `getAllOrders(symbol?)` | string? |
| 获取单个订单 | `GET /api/trading/orders/{id}` | `getOrder(id)` | **string** (long) |
| 取消订单 | `DELETE /api/trading/orders/{id}` | `cancelOrder(orderId)` | **string** (long) |
| 获取用户成交 | `GET /api/trading/trades` | `getUserTrades(symbol?)` | string? |
| 获取用户资产 | `GET /api/trading/assets` | `getAssets()` | - |
| 获取单个资产 | `GET /api/trading/assets/{symbol}` | `getAsset(symbol)` | string |

## 重要注意事项

### 1. ID参数传递
❌ **错误**:
```typescript
// 不要使用number类型
await tradingApi.getOrder(12345);
await tradingApi.cancelOrder(12345);
```

✅ **正确**:
```typescript
// 使用string类型
await tradingApi.getOrder("12345");
await tradingApi.cancelOrder(order.id); // order.id 已经是 string
```

### 2. 比较操作
❌ **错误**:
```typescript
if (order.id === 12345) { /* ... */ }
```

✅ **正确**:
```typescript
if (order.id === "12345") { /* ... */ }
```

### 3. Map键类型
❌ **错误**:
```typescript
const orderMap = new Map<number, Order>();
orderMap.set(order.id, order); // 类型错误
```

✅ **正确**:
```typescript
const orderMap = new Map<string, Order>();
orderMap.set(order.id, order); // 正确
```

### 4. 后端JSON配置 (Program.cs)
```csharp
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // 将long序列化为字符串
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter()
        );
        // 如需要，添加long转string的转换器
    });
```

## 枚举值映射

### OrderSide
- 后端: `0=Buy, 1=Sell`
- 前端: `'buy' | 'sell'`

### OrderType
- 后端: `0=Limit, 1=Market`
- 前端: `'limit' | 'market'`

### OrderStatus
- 后端: `0=Pending, 1=Active, 2=PartiallyFilled, 3=Filled, 4=Cancelled`
- 前端: `'pending' | 'active' | 'partial' | 'filled' | 'cancelled'`

## 迁移检查清单

- [x] 后端所有实体ID改为long类型
- [x] 后端所有DTO的ID改为long类型
- [x] 后端Controller的GetCurrentUserId()返回long
- [x] 前端所有接口定义中的ID改为string
- [x] 前端API调用中的ID参数改为string
- [x] 前端组件中的Map<number, T>改为Map<string, T>
- [x] 前端ID比较操作使用字符串
- [ ] 配置后端JSON序列化器将long转为string (可选，取决于JSON库配置)

## 测试要点

1. **订单提交**: 验证返回的订单ID是字符串
2. **订单查询**: 使用字符串ID查询订单
3. **订单取消**: 使用字符串ID取消订单
4. **资产查询**: 验证资产ID和用户ID为字符串
5. **交易记录**: 验证所有关联ID为字符串
6. **实时推送**: WebSocket推送的数据中ID为字符串

## 更新日志

- **2025-11-22**: 完成前后端ID类型统一
  - 后端: 所有ID统一为long类型
  - 前端: 所有ID统一为string类型
  - 更新所有接口定义和API调用
