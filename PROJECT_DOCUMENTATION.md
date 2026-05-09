# CryptoSpot 项目完整文档

> **加密货币现货交易平台** | .NET 9 + React 18 + MySQL | Clean Architecture + DDD + SignalR

---

## 目录

1. [项目概述](#1-项目概述)
2. [业务领域](#2-业务领域)
3. [技术栈](#3-技术栈)
4. [系统架构](#4-系统架构)
5. [后端详解](#5-后端详解)
   - [Domain 层](#51-domain-层)
   - [Application 层](#52-application-层)
   - [Infrastructure 层](#53-infrastructure-层)
   - [API 层](#54-api-层)
   - [Persistence 层](#55-persistence-层)
6. [前端详解](#6-前端详解)
7. [关键业务流程](#7-关键业务流程)
8. [数据流全景](#8-数据流全景)
9. [配置与部署](#9-配置与部署)

---

## 1. 项目概述

CryptoSpot 是一个基于 .NET 9 后端 + React 18 前端的加密货币现货交易平台。支持用户注册/登录、现货下单（限价/市价）、自动撮合交易、实时行情推送、K 线图表、订单簿深度、用户资产管理等完整交易功能。

**核心能力：**
- 用户认证与授权（JWT）
- 限价单 & 市价单现货交易
- 基于 Channel 的内存撮合引擎（价格优先/时间优先）
- OKX WebSocket 实时行情接入
- SignalR 实时数据推送（K线、深度、成交、ticker）
- TradingView lightweight-charts K 线图表
- 可拖拽调整的交易面板布局
- 自动做市商机器人
- 健康检查 & 启动探针

---

## 2. 业务领域

### 2.1 核心概念

| 概念 | 说明 |
|------|------|
| **交易对** | 如 BTCUSDT，包含 base（BTC）和 quote（USDT）两种资产 |
| **订单** | 用户提交的买卖委托，分为限价单（指定价格）和市价单（按最优价成交） |
| **撮合** | 买卖订单的价格/时间优先匹配过程 |
| **成交** | 撮合成功产生的交易记录 |
| **资产** | 用户持有的各种数字货币余额（可用 + 冻结） |
| **K线** | 按时间周期聚合的 OHLCV 数据 |

### 2.2 订单生命周期

```
用户下单 → Pending → 提交撮合引擎 → Active → 撮合中 → PartiallyFilled → Filled
                                                    ↘ Cancelled（用户取消）
```

### 2.3 用户类型

| 类型 | 说明 |
|------|------|
| Regular | 普通用户 |
| MarketMaker | 做市商账号（自动买卖提供流动性） |
| RiskManagement | 风控账号 |
| LiquidityProvider | 流动性提供商 |
| Admin | 系统管理员 |

---

## 3. 技术栈

### 后端

| 技术 | 用途 |
|------|------|
| .NET 9 | 运行时 |
| ASP.NET Core Web API | REST API |
| SignalR | WebSocket 实时通信 |
| Entity Framework Core | ORM |
| MySQL 8.0 | 数据库 |
| FluentValidation | 请求验证 |
| JWT (Bearer) | 认证 |
| System.Threading.Channels | 撮合引擎队列 |
| Microsoft.Extensions.Http.Resilience | HTTP 重试/熔断 |

### 前端

| 技术 | 用途 |
|------|------|
| React 18 + TypeScript | UI 框架 |
| lightweight-charts (v5) | K 线图渲染 |
| styled-components | CSS-in-JS 样式 |
| @microsoft/signalr | WebSocket 客户端 |
| react-router-dom v6 | 路由 |
| react-query v3 | 服务端状态 |
| axios | HTTP 请求 |
| lucide-react | 图标库 |

### 基础设施

| 技术 | 用途 |
|------|------|
| MySQL 8.0 | 主数据库 |
| OKX WebSocket | 外部行情数据源 |
| Binance REST API | K 线历史数据兜底 |

---

## 4. 系统架构

### 4.1 架构模式：Clean Architecture + DDD

```
┌─────────────────────────────────────────────┐
│               API Layer                     │
│  Controllers, Middleware, Program.cs        │
├─────────────────────────────────────────────┤
│            Application Layer                │
│  Interfaces, DTOs, Validators, Mapping      │
├─────────────────────────────────────────────┤
│           Infrastructure Layer              │
│  Services, MatchEngine, SignalR,            │
│  BackgroundServices, ExternalProviders      │
├─────────────────────────────────────────────┤
│             Domain Layer                    │
│  Entities, ValueObjects, Exceptions         │
├─────────────────────────────────────────────┤
│           Persistence Layer                 │
│  DbContext, Repositories, Configurations    │
└─────────────────────────────────────────────┘
```

**依赖方向：** API → Infrastructure → Application → Domain ← Persistence

### 4.2 项目结构

```
src/
├── CryptoSpot.API/              # Web API 入口
│   ├── Controllers/             # Auth, Trading, KLine, Trade, InternalApi
│   ├── Middleware/               # 全局异常处理
│   └── Program.cs               # 服务注册 + 中间件管道
│
├── CryptoSpot.Application/      # 应用层
│   ├── Abstractions/
│   │   ├── IServices/           # 服务接口 (Auth, Trading, MarketData, RealTime, Users)
│   │   └── Repositories/        # 仓储接口
│   ├── Common/Interfaces/       # ICurrentUserService, IPasswordHasher, ITokenService
│   ├── DTOs/                    # 数据传输对象
│   ├── Mapping/                 # 手动 DTO 映射
│   └── DependencyInjection/     # AddCleanArchitecture()
│
├── CryptoSpot.Domain/           # 领域层
│   ├── Entities/                # User, Order, Trade, Asset, TradingPair, KLineData, BaseEntity
│   ├── ValueObjects/            # Price, Quantity, Money
│   ├── Exceptions/              # DomainException, NotFoundException, etc.
│   └── Extensions/              # DateTimeExtensions
│
├── CryptoSpot.Infrastructure/   # 基础设施层
│   ├── Services/                # Auth, Order, Trade, Asset, Trading, KLine, 撮合适配器
│   ├── MatchEngine/
│   │   ├── Core/                # IOrderBook, InMemoryOrderBook, PriceTimePriorityMatchingAlgorithm
│   │   └── Services/            # ChannelMatchEngineService, TradingPairParser, InMemoryAssetStore
│   ├── BackgroundServices/      # MarketDataStream, PriceUpdateBatch, AutoTrading
│   ├── Hubs/                    # TradingHub (SignalR)
│   ├── ExternalServices/        # BinanceMarketDataProvider, OkxMarketDataStreamProvider
│   ├── Identity/                # JwtTokenService, PasswordHasher, CurrentUserService, JwtSettings
│   ├── HealthChecks/            # 健康检查
│   └── ServiceCollectionExtensions.cs
│
└── CryptoSpot.Persistence/      # 持久化层
    ├── Data/                    # ApplicationDbContext, UnitOfWork
    ├── Repositories/            # 各仓储实现
    └── Configurations/          # Fluent API 实体配置

frontend/
├── src/
│   ├── pages/                   # Trading, Login, Register
│   ├── components/trading/      # ProfessionalKLineChart, OrderBook, TradeForm, etc.
│   ├── hooks/                   # useKLineWithRealTime, useSignalR*, useUserDataStream
│   ├── services/                # signalRClient, tradingService, authService, klineCalculator
│   ├── api/                     # REST API 客户端 (kline, trading, auth, base)
│   ├── contexts/                # AuthContext
│   └── types/                   # TypeScript 类型定义
```

---

## 5. 后端详解

### 5.1 Domain 层

#### 实体

**BaseEntity** — 所有实体的基类：
- `Id` (long, 主键)
- `CreatedAt` / `UpdatedAt` (long, Unix 毫秒时间戳)
- `IsDeleted` (软删除)
- `Version` (byte[], 乐观并发锁 Timestamp)

**User** (表: Users)
```
Id, Username, Email?, PasswordHash?, Type (UserType 枚举),
IsAutoTradingEnabled, MaxRiskRatio, DailyTradingLimit, DailyTradedAmount,
LastLoginAt?, IsActive, CreatedAt, UpdatedAt
```

**TradingPair** (表: TradingPairs)
```
Id, Symbol (如 "BTCUSDT"), BaseAsset, QuoteAsset,
Price, Change24h, Volume24h, High24h, Low24h,
LastUpdated, IsActive, MinQuantity, MaxQuantity, PricePrecision, QuantityPrecision
```

**Order** (表: Orders) — 核心交易实体
```
Id, UserId?, TradingPairId, OrderId (业务号), ClientOrderId?,
Side (Buy/Sell), Type (Limit/Market),
Quantity, Price?, FilledQuantity, AveragePrice,
Status (Pending→Active→PartiallyFilled→Filled/Cancelled/Rejected)
```
领域行为方法：
- `Fill(quantity, price, timestamp)` — 更新成交量和均价，自动切换状态
- `Cancel(timestamp)` — 取消订单（仅非终态可取消）

**Trade** (表: Trades)
```
Id, BuyOrderId, SellOrderId, BuyerId, SellerId,
TradingPairId, TradeId (业务号),
Price, Quantity, Fee, FeeAsset, ExecutedAt
```

**Asset** (表: Assets)
```
Id, UserId?, Symbol, Available, Frozen, MinReserve, TargetBalance, AutoRefillEnabled
// 计算属性: Total = Available + Frozen, UsableBalance = Available - MinReserve
```

**KLineData** (表: KLineData)
```
Id, TradingPairId, TimeFrame (1m/5m/1h...),
OpenTime, CloseTime, Open, High, Low, Close, Volume
```

#### 值对象 (record types)

| 类型 | 说明 |
|------|------|
| `Price(decimal)` | 价格，8 位精度，范围 0~1,000,000，支持算术运算 |
| `Quantity(decimal)` | 数量，同 Price 精度和范围 |
| `Money(decimal, string)` | 带币种的钱，同币种才可运算 |

#### 枚举

```csharp
OrderSide    { Buy=1, Sell=2 }
OrderType    { Limit=1, Market=2 }
OrderStatus  { Pending=1, Active=2, PartiallyFilled=3, Filled=4, Cancelled=5, Rejected=6 }
UserType     { Regular=1, MarketMaker=2, RiskManagement=3, LiquidityProvider=4, Admin=5 }
```

---

### 5.2 Application 层

#### 核心接口

**服务接口：**

| 接口 | 职责 |
|------|------|
| `IAuthService` | 登录/注册/Token 验证/登出 |
| `ITradingService` | 交易门面：订单/资产/交易对/K线一站式查询 |
| `IOrderService` | 订单 CRUD、状态更新 |
| `ITradeService` | 成交记录查询、执行 |
| `IAssetService` | 资产增/减/冻结/解冻/转账 |
| `ITradingPairService` | 交易对查询、价格更新 |
| `IKLineDataService` | K线历史数据查询 |
| `IPriceDataService` | 实时价格服务 |
| `IMatchEngineService` | 撮合引擎：`PlaceOrderAsync` / `EnqueueOrderAsync` |
| `IOrderMatchingEngine` | 旧撮合接口适配器 |
| `IAutoTradingService` | 自动做市商逻辑 |
| `IMarketDataProvider` | Binance REST 行情 |
| `IMarketDataStreamProvider` | OKX WebSocket 行情 |
| `IRealTimeDataPushService` | SignalR 数据推送 |

**仓储接口：** 泛型 `IRepository<T>` + 各实体专属接口（User/TradingPair/Order/Trade/Asset/KLineData）

#### DTO 体系

所有 API 响应统一包装为 `ApiResponseDto<T>`:
```csharp
class ApiResponseDto<T> {
    bool Success; T? Data; string? Message;
    string? Error; string? ErrorCode;
    Dictionary<string, string[]>? ValidationErrors;
}
```

主要 DTO：`OrderDto`, `TradeDto`, `TradingPairDto`, `AssetDto`, `KLineDataDto`, `UserDto`, `OrderBookDepthDto`, `MarketTradeDto` 等。

---

### 5.3 Infrastructure 层

#### 5.3.1 撮合引擎（核心）

两套引擎并存：
- **旧引擎** (`Matching/InMemoryMatchingEngine`)：基于 Domain.Matching 模型
- **新引擎** (`MatchEngine/`)：基于 Channel 的纯内存撮合

**新引擎架构：**

```
ChannelMatchEngineService (Singleton, IAsyncDisposable)
├── ConcurrentDictionary<string, IOrderBook>      # 每交易对一个订单簿
├── ConcurrentDictionary<string, Channel<OrderRequest>>  # 每交易对一个 Channel(容量10000)
├── ConcurrentDictionary<string, Task>            # 每交易对一个处理任务
├── InMemoryAssetStore                            # 内存资产（冻结/解冻/结算）
├── PriceTimePriorityMatchingAlgorithm            # 价格优先+时间优先
└── TradingPairParserService                      # 交易对解析
```

**InMemoryOrderBook**：`SortedDictionary<decimal, Queue<Order>>` 分别维护买盘（降序）和卖盘（升序），`GetDepth` 加锁保证读安全。

**撮合流程：**
1. `PlaceOrderAsync` → 验证 → 冻结内存资产 → order.Status=Active → 写入 Channel
2. 后台 Task 从 Channel 读取 → `ProcessSingleOrderAsync`
3. `IOrderBook.Add(taker)` → `IMatchingAlgorithm.Match()` → 逐个 MatchSlice
4. 每片：`SettleTradeAsync`（内存结算）→ 创建 Trade → 更新 Order 状态 → `PublishTradeEventsAsync`
5. 推送：lastPrice/midPrice → SignalR ticker 组；OrderUpdate → user 组；UserTradeUpdate → 双方用户
6. `PersistToDatabaseAsync`：await 写 DB（非 fire-and-forget）

**撮合算法 (`PriceTimePriorityMatchingAlgorithm`)：**
- 从订单簿获取最优对手价（`GetBestOpposite`）
- 价格交叉检查：买单 `taker.Price >= maker.Price`，卖单 `taker.Price <= maker.Price`
- 市价单无条件匹配
- 自成交避免：跳过同 UserId 的 maker
- 取 min(剩余 taker, 剩余 maker) 为成交量
- 返回 `IEnumerable<MatchSlice>`，外部迭代消费

#### 5.3.2 后台服务

| 服务 | 类型 | 功能 |
|------|------|------|
| `MarketDataStreamService` | BackgroundService | 连接 OKX WebSocket，订阅 ticker/深度/成交/1mK线，推送到 SignalR + 持久化 K 线到 DB |
| `PriceUpdateBatchService` | BackgroundService | Channel 批量处理价格更新（50条或100ms），去重后写 DB + 推 ticker |
| `AutoTradingService` | BackgroundService | 包装 AutoTradingLogicService 为 HostedService |
| `AutoTradingLogicService` | Singleton 服务 | 每30秒循环：获取活跃交易对 → 匹配订单 → 创建做市单（±0.05% 限价单） |

#### 5.3.3 外部行情接入

**OKX WebSocket** (`OkxMarketDataStreamProvider`)：
- 双连接：public WS（ticker/深度/成交）+ business WS（标记价格 K 线）
- 自动重连 + 重新订阅
- 事件驱动：OnTicker / OnOrderBook / OnTrade / OnKLine

**Binance REST** (`BinanceMarketDataProvider`)：
- HTTP 调用 `/api/v3/ticker/24hr` 和 `/api/v3/klines`
- 支持 HTTP 代理（`Binance:ProxyUrl`）
- `AddStandardResilienceHandler()` 重试+熔断

#### 5.3.4 SignalR 实时推送

**TradingHub** (`/tradingHub`)：

| Hub 方法 | 分组 | 鉴权 | 推送事件 |
|----------|------|------|----------|
| SubscribeKLineData | `kline_{symbol}_{interval}` | 无 | KLineUpdate |
| SubscribePriceData | `price_{symbol}` | 无 | PriceUpdate |
| SubscribeOrderBook | `orderbook_{symbol}` | 无 | OrderBookData/Update |
| SubscribeTicker | `ticker_{symbol}` | 无 | LastTradeAndMid |
| SubscribeTrades | `trades_{symbol}` | 无 | TradeUpdate |
| SubscribeUserData | `user_{userId}` | **[Authorize]** | OrderUpdate, UserTradeUpdate, AssetUpdate |

**SignalRDataPushService** 封装 `IHubContext<TradingHub>`，按分组推送。

#### 5.3.5 身份认证

- `JwtTokenService`：HMAC-SHA256 签名，Claims: NameIdentifier(userId) + Name(username) + Jti
- `PasswordHasher`：PBKDF2+SHA256，100,000 迭代，16 字节盐，32 字节哈希
- `CurrentUserService`：从 `HttpContext.User` 提取 userId/username
- JWT 配置同时支持 Authorization header（REST）和 `access_token` query string（SignalR WebSocket）

#### 5.3.6 服务注册（生命周期）

**Scoped（每请求）：**
所有 Repository、ApplicationService、IUnitOfWork、ApplicationDbContext

**Singleton（全局）：**
```
InMemoryAssetStore, ITradingPairParser, IMatchingAlgorithm,
IMatchEngineService (ChannelMatchEngineService),
IAutoTradingService, PriceUpdateBatchService,
IRealTimeDataPushService, IMarketDataStreamProvider,
IDtoMappingService, TimeProvider.System
```

---

### 5.4 API 层

#### 中间件管道

```
ExceptionHandling → Swagger(Dev) → HTTPS → CORS → Routing → RateLimiter → Auth → Endpoints
```

#### 全局异常映射

| 异常 | HTTP 状态码 |
|------|------------|
| ValidationException | 400 |
| NotFoundException | 404 |
| UnauthorizedException | 403 |
| UnauthorizedAccessException | 401 |
| BusinessException | 400 |
| 其他 | 500 |

#### Controller 路由表

| Controller | 路由前缀 | 鉴权 |
|-----------|---------|------|
| AuthController | `api/auth` | 部分匿名 |
| TradingController | `api/trading` | 部分匿名 |
| KLineController | `api/kline` | 匿名 |
| TradeController | `api/v2/trade` | 需鉴权 |
| InternalApiController | `api/internal` | 内部 |

#### 速率限制

登录/注册端点：固定窗口，5次/分钟。

---

### 5.5 Persistence 层

#### DbContext

`ApplicationDbContext` (MySQL 8.0, Pomelo.EntityFrameworkCore.MySql)
- `IDbContextFactory<T>` 池化工厂，poolSize=30
- 重试策略：3次，间隔5秒
- Fluent API 配置：各实体独立配置类

#### 仓储基类

`BaseRepository<T>` 每个操作创建新的 DbContext 实例（通过工厂），写操作立即 SaveChanges。

#### 关键索引

| 表 | 唯一索引 | 普通索引 |
|----|---------|---------|
| Users | Username, Email | — |
| TradingPairs | Symbol | — |
| Assets | UserId+Symbol | — |
| Orders | OrderId | UserId+Status |
| Trades | TradeId | ExecutedAt |
| KLineData | — | TradingPairId+TimeFrame+OpenTime |

---

## 6. 前端详解

### 6.1 页面结构

```
App.tsx (路由 + AuthProvider)
├── /login    → Login.tsx
├── /register → Register.tsx
└── /trading  → Trading.tsx
    ├── LeftPanel
    │   ├── TradingHeader (交易对选择 + 实时行情)
    │   ├── ProfessionalKLineChart (lightweight-charts)
    │   └── AccountTabs (委托/历史/成交/资产)
    ├── ResizeHandle (可拖拽分隔条)
    └── RightPanel (可调宽度)
        ├── OrderBook (盘口深度)
        ├── RecentTrades (实时成交)
        └── TradeForm (下单面板)
```

### 6.2 组件说明

| 组件 | 功能 |
|------|------|
| **ProfessionalKLineChart** | lightweight-charts v5 渲染：蜡烛图 + 成交量 + MA5/MA10/MA30。支持缩放/平移/十字光标/全屏。数据秒级去重，时区 Asia/Shanghai |
| **OrderBook** | 买卖盘口深度，SignalR 实时增量更新，深度条可视化 |
| **TradeForm** | 买入/卖出 限价/市价下单。百分比快速填单。接入 `useSignalRTicker` 实时市价 |
| **RecentTrades** | 最近 80 条公开成交，实时推送 |
| **AccountTabs** | 4 标签：当前委托(数量+状态) / 历史委托 / 成交记录 / 我的资产。15s 轮询 + SignalR 实时 |
| **TradingHeader** | 交易对下拉（6个）、合并行情数据、用户信息 |

### 6.3 数据流

```
后端 REST API ──→ api/*.ts ──→ services/*.ts ──→ hooks/*.ts ──→ Components
后端 SignalR  ──→ signalRClient.ts ──→ hooks/useSignalR*.ts ──→ Components
                                                          └──→ Context(Auth)
```

**三层数据获取模式（大多数数据组件）：**
1. HTTP 快照（初始加载）
2. SignalR 实时流（增量更新）
3. 合并去重展示

### 6.4 SignalR 订阅全景

| 前端 Hook | 订阅方法 | 用途 |
|-----------|---------|------|
| `useKLineWithRealTime` | SubscribeKLineData + REST | K 线图数据 |
| `useSignalRPriceData` | SubscribePriceData | 实时价格 |
| `useSignalROrderBook` | SubscribeOrderBook | 订单簿增量 |
| `useSignalRTicker` | SubscribeTicker | 最新成交价/中间价 |
| `useMergedTickerData` | PriceData + Ticker | 合并行情（Header） |
| `useUserDataStream` | SubscribeUserData | 用户订单/成交/资产 |

### 6.5 状态管理

- **AuthContext**：全局用户认证状态（React Context）
- **组件本地状态**：`useState` + `useRef`
- **服务端缓存**：`tradingService` 内部 5 秒内存缓存
- 无 Redux/Zustand 等外部状态库

### 6.6 API 回退策略

| 场景 | 主路径 | 回退 |
|------|--------|------|
| K 线历史 | 后端 `/api/kline/history` | Binance 公开 API `api.binance.com/api/v3/klines` |
| 市价参考 | SignalR Ticker `lastPrice` | REST `getTradingPair().price` |

---

## 7. 关键业务流程

### 7.1 用户下单完整流程

```
用户点击买入/卖出
  │
  ▼
TradeForm.handleSubmit()
  → tradingService.submitOrder()
    → POST /api/trading/orders
      ▼
TradingController.SubmitOrder()
  → TradingService.SubmitOrderAsync()
    → OrderService.CreateOrderDtoAsync()
      1. 验证交易对存在/活跃
      2. 精度截断 quantity/price
      3. 冻结资产 (IAssetService.FreezeAssetAsync → DB)
      4. 保存订单 (status=Pending → DB)
      5. 提交撮合引擎 (IMatchEngineService.EnqueueOrderAsync)
         → ChannelMatchEngineService
           order.Status = Active
           写入 per-symbol Channel
  │
  ▼
后台撮合线程 ProcessSingleOrderAsync:
  1. 加入订单簿
  2. IMatchingAlgorithm.Match() 逐片匹配
  3. SettleTradeAsync (内存结算)
  4. 创建 Trade 记录
  5. UpdateOrderStatus (Filled/PartiallyFilled)
  6. 推 SignalR:
     - LastTradeAndMid → ticker_{symbol}
     - OrderUpdate → user_{maker/taker}
     - UserTradeUpdate → user_{maker/taker}
  7. await PersistToDatabaseAsync (Trade + Order 状态 → DB)
  │
  ▼
前端实时更新:
  - OrderBook 刷新
  - AccountTabs 订单状态更新
  - K 线图最新成交标记
```

### 7.2 实时行情数据流

```
OKX WebSocket
  │
  ▼
OkxMarketDataStreamProvider
  OnTicker  → PriceUpdateBatchService → DB + SignalR PriceUpdate
  OnOrderBook → SignalR OrderBookData
  OnTrade → SignalR TradeUpdate
  OnKLine → SignalR KLineUpdate + IKLineDataRepository.Upsert (持久化)
  │
  ▼
TradingHub (SignalR) → 各 group
  │
  ▼
前端 signalRClient → useSignalR* hooks → Components
```

### 7.3 用户认证流程

```
注册: POST /api/auth/register
  → AuthService.RegisterAsync
    1. 检查用户名/邮箱唯一性
    2. PBKDF2 哈希密码
    3. 保存用户
    4. 生成 JWT (7天过期)
    5. 返回 token + user

登录: POST /api/auth/login
  → AuthService.LoginAsync
    1. 用户名或邮箱查找用户
    2. PBKDF2 验证密码
    3. 更新最后登录时间
    4. 生成 JWT
    5. 返回 token + user

后续请求:
  REST: Authorization: Bearer <token>
  SignalR: accessTokenFactory → ?access_token=<token>
```

---

## 8. 数据流全景

```
┌─────────────────────────────────────────────────────────────────┐
│                         外部数据源                                │
│  OKX WebSocket (行情)         Binance REST (K线兜底)             │
└──────────┬──────────────────────────┬───────────────────────────┘
           │                          │
           ▼                          ▼
    MarketDataStreamService    BinanceMarketDataProvider
           │                          │
           ▼                          ▼
    SignalRDataPushService    KLineApiService (前端兜底)
           │
           ▼
    TradingHub ──────────────────► 前端 SignalR 订阅
    │                                    │
    │ KLineUpdate                        ▼
    │ PriceUpdate                  useSignalR* hooks
    │ OrderBookData                      │
    │ LastTradeAndMid                    ▼
    │ TradeUpdate                   Components 渲染
    │ OrderUpdate
    │ UserTradeUpdate
    │ AssetUpdate
    │
    ▼
  前端 REST API 轮询 (15s/30s) → 补充数据缺口

═══════════════════════════════════════════════════════════════════

                          撮合引擎（内存）

  用户下单 → OrderService ──→ ChannelMatchEngineService
                                   │
                                   ▼
                            Channel<OrderRequest>
                                   │
                                   ▼
                            ProcessSingleOrderAsync
                              ├── InMemoryOrderBook.Add
                              ├── MatchingAlgorithm.Match
                              ├── InMemoryAssetStore.Settle
                              ├── PublishTradeEventsAsync
                              │     ├── PushLastTradeAndMidPriceAsync
                              │     ├── PushUserOrderUpdateAsync
                              │     └── PushUserTradeAsync
                              └── PersistToDatabaseAsync → MySQL
```

---

## 9. 配置与部署

### 9.1 配置文件 (`appsettings.json`)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=CryptoSpotDb;Uid=root;Pwd=***;"
  },
  "JwtSettings": {
    "SecretKey": "<至少32字符>",
    "Issuer": "CryptoSpot",
    "Audience": "CryptoSpotUsers",
    "ExpiryInDays": 7
  },
  "Binance": {
    "ProxyUrl": "http://127.0.0.1:7890"
  },
  "MarketMakers": {
    "UserIds": [1]
  },
  "HealthChecks": {
    "EnableStartupCheck": true,
    "FailFast": true,
    "MaxRetries": 3,
    "RetryDelaySeconds": 5
  }
}
```

### 9.2 前端环境变量 (`.env`)

```
REACT_APP_API_URL=http://localhost:5000/api
REACT_APP_SIGNALR_URL=http://localhost:5000/tradingHub
PORT=3000
```

### 9.3 健康检查端点

| 端点 | 用途 |
|------|------|
| `/health` | 完整检查报告 |
| `/health/ready` | K8s Readiness Probe（仅 DB 检查） |
| `/health/live` | K8s Liveness Probe（总是健康） |

### 9.4 数据库初始化

启动时自动执行 `context.Database.Migrate()` 运行 EF 迁移。然后 `DataInitializationService` 检查是否需要种子数据：
- 3 个交易对：BTCUSDT, ETHUSDT, SOLUSDT
- 2 个系统用户：MarketMaker + Admin
- 3 个测试用户
- 对应资产余额

### 9.5 启动顺序

1. MySQL 必须可用
2. `dotnet run` 启动后端 → 健康检查等待 DB 就绪
3. `npm start` 启动前端 (port 3000)
4. 浏览器打开 `http://localhost:3000/trading`

---

## 附录

### 已知待改进项

1. **撮合引擎**：两套引擎并存，旧 `InMemoryMatchingEngine` 可删除
2. **资产系统**：DB 资产和内存资产（InMemoryAssetStore）独立，初始化/同步逻辑待完善
3. **K 线数据**：依赖 OKX WebSocket 实时累积，历史数据有限，前端有 Binance 兜底
4. **订单状态推送**：用户特定事件（OrderUpdate/UserTradeUpdate）仅在撮合成交时推送，取消等操作暂无实时推送
5. **前端严格模式**：TypeScript `strict: true` 但部分 `any` 类型仍存在
6. **可观测性**：无 OpenTelemetry/分布式追踪，日志以 Console 为主
