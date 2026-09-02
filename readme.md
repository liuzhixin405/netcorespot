# CryptoSpot —— 数字资产现货交易平台（AI 增强版）

> .NET 9 + React 现货交易演示系统。内置 **AI 行情分析 / AI 实时对话 / AI 交易助手（自然语言下单）**，以及内存撮合、OKX 实时行情、做市商自动交易、风控监控、回测与信号系统。本仓库代码已完整实现 AI 功能（见 [`docs/AI_INTEGRATION_DESIGN.md`](docs/AI_INTEGRATION_DESIGN.md)）。

## 项目简介

CryptoSpot 是一个模拟数字资产现货交易平台：

- **交易核心**：BTCUSDT / ETHUSDT / SOLUSDT 三个交易对，限价单 / 市价单，撮合引擎为进程内 Channel + 价格时间优先算法（不依赖 Redis，配置文件中 Redis 段仅为历史保留）。
- **实时行情**：OKX WebSocket（公共行情 + business K 线）+ Binance REST 兜底，实时推送价格、订单簿、成交、K 线。
- **AI 增强**：在交易平台之上叠加了一套完整可用的 AI 助手能力——用自然语言分析行情、查询资产、下达交易指令，配合风控阈值、人工审批和全链路审计。
- **量化演示**：做市商自动交易（市场做市策略）、风险监控事件、回测与信号查询。
- **实时推送**：SignalR + MessagePack（`/tradingHub`），订阅价格 / 订单簿 / K 线 / 用户订单 / 风控事件。

> ⚠️ 本项目为学习/演示用途，撮合与行情均为模拟/公共数据，不构成任何投资建议。

## 主要功能

### 1. AI 功能（已完整实现）

| 能力 | 说明 |
| --- | --- |
| **AI 行情分析** | `POST /api/ai/analyze`：模型通过只读工具获取真实行情与持仓后，给出分析结论，并附带数据引用（`Citations`），禁止编造行情。 |
| **AI 实时对话** | `POST /api/ai/chat`：SSE 流式输出（`event: message` / `event: done`），对话持久化，支持 `conversationId` 续聊。 |
| **AI 交易助手** | `POST /api/ai/trade`：解析自然语言指令（如“用 100 USDT 市价买入 BTC”），自主规划并使用 `place_order` / `cancel_order` / `cancel_all_orders` 工具执行。 |
| **人工审批** | 写操作触发风控阈值后自动生成审批单（`Pending`），用户通过 `POST /api/ai/approvals/{id}/approve` / `reject` 决定是否放行。 |
| **审计追踪** | `GET /api/ai/audit`：记录 `ai.analyze`、`ai.trade.plan`、`ai.approval.approved/rejected` 等全链路操作。 |
| **对话持久化** | 用户会话（AiConversations / AiMessages），`GET /api/ai/conversations`、`GET /api/ai/conversations/{id}`。 |
| **平台文档助手** | `POST /api/ai/assistant`：基于仓库文档回答产品/平台问题。 |
| **新闻情绪分析** | `POST /api/ai/news/sentiment`：对标题/正文给出利好/利空/中性判断与评分。 |
| **持仓报告** | `POST /api/ai/portfolio/report`：基于真实资产与成交生成中文持仓报告。 |
| **模型容错** | OpenAI 兼容协议（默认 Ollama `qwen2.5:7b`），支持故障熔断（连续失败 3 次熔断 60 秒）与备用模型（`FallbackModels`）。 |

### 2. 交易功能

- 限价单 / 市价单下单、撤单、批量撤单（`DELETE /api/trading/orders/batch`）。
- 订单簿（前 N 档深度）、实时 K 线、最新成交、24h/实时行情汇总。
- **双版本 API**：v1 `api/trading/*`（完整业务版）与 v2 `api/v2/trade/*`（精简下单版）。
- 测试下单接口 `POST /api/trading/orders/test`（不实际撮合）。
- 用户资产：可用 / 冻结 / 币种资产 / 资产汇总 / 成交统计。

### 3. 行情与市场数据

- **OKX WebSocket 实时订阅**（公共频道 + business K 线频道）驱动内存订单簿与实时推送。
- **Binance REST 兜底**：历史 K 线缺失/网络失败时回源（支持代理配置 `Binance:ProxyUrl`）。
- K 线多周期（`GET /api/kline/intervals` 返回支持列表）、最新 K 线、历史分页。

### 4. 做市与风控

- **做市商自动交易**：`MarketMakers` 配置 + 波动率感知做市策略（Avellaneda-Stoikov 风格价差/库存参数见配置段）。
- **风险监控**（`RiskMonitorService`，默认 5 秒扫描一次）：闪崩、价格偏离、订单洪峰、刷量、持仓集中，产生 `RiskEvent` 并实时推送。
- 风险事件查询 / 仪表盘 / 处理：`GET /api/risk/events`、`GET /api/risk/dashboard`、`POST /api/risk/events/{id}/handle`。

### 5. 回测与信号

- 回测：`POST /api/backtest`、`GET /api/backtest/{id}`、`GET /api/backtest/strategies`。
- 信号：`GET /api/signals/{symbol}`（最新信号）、`GET /api/signals/{symbol}/history`。

### 6. 前端（React SPA）

- 登录 / 注册、交易面板（K 线图、订单簿、下单表单、持仓/订单/成交）。
- **AI 助手面板**（`src/components/ai/AiAssistantPanel.tsx`）：行情分析、自然语言下单、AI 对话、审批操作、审计查看。
- 实时推送接入（SignalR），`lightweight-charts` 渲染图表。

## 技术栈

| 层 | 技术 |
| --- | --- |
| 后端 | .NET 9 / ASP.NET Core，EF Core 9 + Pomelo MySQL，JWT 认证 |
| 实时 | SignalR + MessagePack（`/tradingHub`） |
| AI | OpenAI 兼容 `chat/completions`（Ollama / DeepSeek / 其它 /v1 端点），函数调用（tools） |
| 中间件 | Swagger、响应压缩、输出缓存、限流（RateLimiter）、健康检查、全局异常处理 |
| 前端 | React 18 + TypeScript（CRA），styled-components，lightweight-charts，axios，@microsoft/signalr |
| 数据库 | MySQL 8（默认库 `CryptoSpotDb`，开发库 `CryptoSpotDb_Dev`） |

## 目录结构

```
├─ src/
│  ├─ CryptoSpot.Domain/            领域模型（实体、枚举、仓储接口）
│  ├─ CryptoSpot.Application/       DTO、服务抽象（Trading、AI、Risk、Backtest、Signals、Auth…）
│  ├─ CryptoSpot.Infrastructure/    实现层：撮合引擎、行情源、AI、SignalR、EF Core、后台服务
│  ├─ CryptoSpot.Persistence/       数据库上下文与仓储实现
│  └─ CryptoSpot.API/               控制器、中间件、Program.cs（启动入口）
├─ frontend/                        React SPA（AI 助手面板在 components/ai）
├─ docs/                            设计文档（AI、架构、API 映射、数据库修复、撮合重构、优化计划）
├─ scripts/                         数据库修复/初始化脚本（fix-user-table.sql、init-database.sql）
└─ CryptoSpot.sln / README.md
```

关键后端目录：

- `Infrastructure/Matching/`：进程内撮合（Channel + 价格时间优先），异步持久化。
- `Infrastructure/MarketData/`：OKX WebSocket / Binance REST 行情客户端、订单簿内存簿。
- `Infrastructure/Ai/`：AI 模型客户端、分析器、交易规划、审批、审计、会话、工具执行、扩展能力。
- `Infrastructure/Background/`：行情推送、价格批量更新、做市自动交易、风控扫描、撮合初始化等后台服务。
- `Infrastructure/Hubs/TradingHub.cs`：SignalR 订阅/推送。
## 快速开始

### 环境要求

- .NET 9 SDK
- MySQL 8.x（默认连接 `Server=localhost;Database=CryptoSpotDb_Dev;Uid=root;Pwd=123456;`，开发环境在 `appsettings.Development.json` 中为 `CryptoSpotDb_Dev`，正式配置为 `CryptoSpotDb`）
- Node.js 18+（仅前端需要）

### 1. 启动后端 API

```bash
dotnet run --project src/CryptoSpot.API
# 默认监听 http://localhost:5000（launchSettings 的 http profile）
# Swagger: http://localhost:5000/swagger
```

首次启动会自动：

1. 应用 EF Core 迁移（`InitDbContext`，失败仅告警不阻断）。
2. 若库中无活跃交易对或系统用户（`NeedsInitializationAsync`），自动初始化种子数据：
   - 交易对：BTCUSDT、ETHUSDT、SOLUSDT
   - 系统用户：SystemMarketMaker（做市商）、SystemAdmin（管理员）
   - 测试用户：test_user_1 / test_user_2 / test_user_3（Regular，各带 USDT 10,000 / BTC 1 / ETH 10 / SOL 100 初始资产）
   - 做市商系统资产：USDT 1,000,000 / BTC 100 / ETH 5,000 / SOL 50,000

> ⚠️ 首次启动自动创建的用户**不包含 Email 与密码哈希**（`Users` 表历史缺列，详见 [`docs/fix-password-hash-error.md`](docs/fix-password-hash-error.md)）。若要直接登录测试账号，请先执行一次修复脚本（与官方文档一致，脚本内含 `USE cryptospot;`，如开发库名为 `CryptoSpotDb_Dev` 请先改脚本首行或改用 `scripts/init-database.sql` 全量初始化）：
> ```bash
> mysql -u root -p cryptospot < scripts/fix-user-table.sql
> # 或（全量重建示例库）mysql -u root -p < scripts/init-database.sql
> ```
> 脚本为账号补齐 Email/密码哈希（哈希格式为 PBKDF2-SHA256 100k 迭代 Base64）。
> **DEBUG 构建中密码校验恒为通过**（`PasswordHasher.Verify` 在 `#if DEBUG` 下直接返回 `true`），因此本地调试时只需保证密码哈希非空即可登录；正式（Release）构建必须使用脚本中的真实哈希。

- 测试账号（fix 脚本写入的密码）：`test_user_1 / test123`、`SystemAdmin / admin123`、`SystemMarketMaker / maker123`。
- 也可以直接调用 `POST /api/auth/register` 注册新账号（注册用户无初始资产，可通过内部接口 `POST /api/internal/assets/batch-update` 充值体验）。

### 2. 配置 AI 模型

默认配置指向本机 Ollama（OpenAI 兼容端点）：

```jsonc
"Ai": {
  "Enabled": true,
  "BaseUrl": "http://localhost:11434/v1",   // OpenAI 兼容 /v1
  "ApiKey": "ollama",
  "Model": "qwen2.5:7b",
  "Temperature": 0.2,
  "MaxTokens": 2048,
  "TimeoutSeconds": 120,
  "CircuitBreakerFailureThreshold": 3,     // 连续失败 3 次进入熔断
  "CircuitBreakerCooldownSeconds": 60,     // 熔断冷却 60 秒
  "FallbackModels": []                     // 备用模型列表（可选）
}
```

- 本地 Ollama：`ollama pull qwen2.5:7b`，保持服务运行（默认 11434）。
- 也可改为任意 OpenAI 兼容服务：把 `BaseUrl` 指向 `https://api.deepseek.com/v1`（`ApiKey` 填 DeepSeek Key）、或 GLM/Qwen 等 /v1 端点。
- 不启动 AI 时，将 `Ai.Enabled` 置为 `false`，其余交易/行情功能不受影响。

### 3. 启动前端

```bash
cd frontend
npm install
npm start          # http://localhost:3000
```

> 前端 `package.json` 的 `proxy` 默认指向 `https://localhost:5001`。若你按上面方式将 API 跑在 `http://localhost:5000`，请同步把 proxy 改为 `http://localhost:5000`（或用 `dotnet run --urls https://localhost:5001` 启动 API，并信任开发证书）。

### 4. 体验 AI

登录后进入交易页，在右侧 AI 面板可尝试：

- **行情分析**：输入“分析一下 BTCUSDT 最近走势并给出参考”，选择 BTCUSDT。
- **AI 对话**：任意提问，逐字 SSE 流式返回。
- **自然语言下单**：输入“用 100 USDT 市价买入 BTC”（小额无需审批，直接执行；超过阈值会生成审批单，需手动批准后才会真正下单）。
- **审批/审计**：查看待审批项与审计记录。

## AI 功能详解

### 架构

```
用户请求
  ├─ /api/ai/analyze ──> AiAnalyzer ──> 只读工具(get_ticker/get_klines/get_orderbook/get_assets/get_open_orders/get_recent_trades)
  ├─ /api/ai/chat    ──> 模型流式对话（SSE）＋ 会话持久化
  ├─ /api/ai/trade   ──> AiTradeService.PlanAndExecuteAsync
  │                      ├─ 模型规划：调用只读工具 + 写工具(place_order/cancel_order/cancel_all_orders)
  │                      ├─ AiGuardrail 风控判定（阈值见配置）
  │                      │     ├─ 直接执行（小额/正常）
  │                      │     └─ 需要审批 ──> AiApprovalService 创建 Pending 审批单
  │                      └─ 返回执行摘要 + 待审批列表
  ├─ /api/ai/approvals/{id}/approve|reject
  │                      └─ AiTradeService.ExecuteApprovedAsync（批准后真正下单/撤单）
  └─ 所有关键动作 ──> AiAuditService 写入审计
```

- **只读工具**（不产生写操作，安全）：`get_ticker`、`get_klines`（最近 24 根 1h）、`get_orderbook`（前 10 档）、`get_assets`、`get_open_orders`、`get_recent_trades`（最近 50 笔）。
- **写工具**：`place_order`（symbol / side / type / quantity / price）、`cancel_order(orderId)`、`cancel_all_orders(symbol?)`。
- 模型工具调用结果**必须来自真实数据**；系统提示词要求“不编造行情、不擅自交易、用中文回答”。

### 风控阈值（appsettings `AiGuardrail`）

| 配置 | 默认 | 说明 |
| --- | --- | --- |
| `MarketOrderApprovalThreshold` | 1000 | 市价单金额 ≥ 1000 USDT 需人工审批 |
| `LimitOrderApprovalThreshold` | 5000 | 限价单金额 ≥ 5000 USDT 需人工审批 |
| `MaxPriceDeviationPercent` | 5 | 限价偏离最新价 > 5% 直接拒绝 |
| `MaxOrdersPerMinute` | 5 | 每分钟下单 > 5 笔直接拒绝 |
| `DailyTradeApprovalThreshold` | 10000 | 当日累计成交金额 ≥ 10000 USDT 后需审批 |

### 会话与审计

- 每个 AI 会话写入 `AiConversations`（含标题/时间），消息写入 `AiMessages`。
- 审计条目包含：动作类型（`ai.analyze`、`ai.trade.plan`、`ai.approval.approved/rejected` 等）、目标工具、请求/响应 JSON、耗时、状态。
## API 速查

### 认证 Auth（`api/auth`）

| 方法 | 路由 | 说明 |
| --- | --- | --- |
| POST | `register` | 注册（username / email / password） |
| POST | `login` | 登录返回 JWT |
| GET | `me` | 当前用户信息 |
| POST | `logout` | 注销 |

### 交易 v1 Trading（`api/trading`）

| 方法 | 路由 | 说明 |
| --- | --- | --- |
| GET | `pairs` / `pairs/{symbol}` / `pairs/summary` | 交易对、详情、汇总 |
| GET | `klines/{symbol}`、`klines/{symbol}/latest` | K 线（支持 interval/limit） |
| GET | `assets`、`assets/summary` | 用户资产/汇总 |
| GET/POST | `orders` | 订单列表 / 下单 |
| GET | `orders/open` | 未成交订单 |
| GET | `orders/{orderId}` | 订单详情 |
| DELETE | `orders/{orderId}`、`orders/batch` | 撤单 / 批量撤单 |
| POST | `orders/test` | 测试下单（不撮合） |
| GET | `trades`、`orders/{orderId}/trades`、`trades/statistics` | 成交记录/统计 |
| GET | `orderbook/{symbol}` | 订单簿（depth） |
| GET | `market/trades/{symbol}` | 市场最近成交 |

### 交易 v2 Trade（`api/v2/trade`，精简版）

| 方法 | 路由 | 说明 |
| --- | --- | --- |
| POST | `orders` | 下单（limit/market, buy/sell） |
| DELETE | `orders/{orderId}` | 撤单 |
| GET | `orders` | 用户订单 |
| GET | `orderbook/{symbol}` | 订单簿（depth） |
| GET | `trades` | 用户成交 |

### K 线 KLine（`api/kline`）

| 方法 | 路由 | 说明 |
| --- | --- | --- |
| GET | `history` | K 线历史（symbol / interval / limit） |
| GET | `latest` | 最新 K 线 |
| GET | `intervals` | 支持的时间周期 |

### AI（`api/ai`，均需登录）

| 方法 | 路由 | 说明 |
| --- | --- | --- |
| POST | `analyze` | AI 行情分析（`{question, symbol, conversationId?}`） |
| GET | `conversations` | 我的会话列表 |
| GET | `conversations/{id}` | 会话消息 |
| GET | `audit` | 审计记录 |
| POST | `trade` | AI 自然语言交易（`{instruction, symbol}`） |
| POST | `chat` | SSE 对话（`{message, conversationId?}`） |
| POST | `approvals/{id}/approve` / `reject` | 审批 / 拒绝 |
| POST | `assistant` | 文档助手（`{question}`） |
| POST | `news/sentiment` | 新闻情绪（`{headline, content?, symbol?}`） |
| POST | `portfolio/report` | 持仓报告 |

**SSE 协议**（`POST /api/ai/chat`）：

```
event: message
data: {"contentDelta":"...","reasoningContent":"..."}

event: done
data: {"conversationId":123}
```

### 回测/信号/风控/内部

| 方法 | 路由 | 说明 |
| --- | --- | --- |
| POST | `api/backtest` | 运行回测 |
| GET | `api/backtest/{id}` | 回测结果 |
| GET | `api/backtest/strategies` | 策略列表 |
| GET | `api/signals/{symbol}`、`api/signals/{symbol}/history` | 信号 |
| GET | `api/risk/events`、`api/risk/dashboard` | 风控事件/仪表盘 |
| POST | `api/risk/events/{id}/handle` | 处理风控事件 |
| GET | `api/internal/trading-pairs` | 内部：交易对 |
| GET | `api/internal/assets` | 内部：全部资产 |
| POST | `api/internal/assets/batch-update` | 内部：批量调整资产（充值体验） |

## SignalR 实时事件

Hub 路径：`/tradingHub`（MessagePack 协议，需携带 JWT）。

| 客户端方法（订阅/取消） | 服务器推送事件 |
| --- | --- |
| `SubscribeKLineData(symbol, interval)` / `UnsubscribeKLineData` | `KLineSubscribed` / `KLineUnsubscribed` / `KLineUpdate` |
| `SubscribePriceData(symbols[])` / `UnsubscribePriceData` | `PriceSubscribed` / `PriceUnsubscribed` / `PriceUpdate` |
| `SubscribeOrderBook(symbol, depth)` / `UnsubscribeOrderBook` | `OrderBookSubscribed` / `OrderBookUnsubscribed` / `OrderBookData` / `OrderBookUpdate` |
| `SubscribeTicker(symbol)` / `UnsubscribeTicker` | `TickerSubscribed` / `TickerUnsubscribed` |
| `SubscribeTrades(symbol)` / `UnsubscribeTrades` | `TradesSubscribed` / `TradesUnsubscribed` / `TradeUpdate` |
| `SubscribeRiskAlerts()` / `UnsubscribeRiskAlerts` | `RiskAlertsSubscribed` / `RiskAlertsUnsubscribed` / `RiskAlert` |
| `SubscribeUserData()` / `UnsubscribeUserData` | `UserDataSubscribed` / `UserDataUnsubscribed` / `OrderUpdate` / `AssetUpdate` / `UserTradeUpdate` / `LastTradeAndMid` |
| 通用 | `JoinGroup` / `LeaveGroup` / `Connected` / `Error` |

## 数据库说明

- EF Core 迁移在启动时自动应用；表结构以迁移为准。
- 主要实体：`TradingPair`、`User`（Regular / Admin / MarketMaker）、`Asset`、`Order`、`Trade`、`KLineData`、`RiskEvent`、`BacktestRun`、`Signal`、`AiConversation`、`AiMessage`、`AiApproval`、`AiAuditLog` 等（`Domain/Entities`）。
- 资产撮合与行情为主内存 + 异步落库：`Asset` 在内存 `InMemoryAssetStore` 中实时更新，并由后台服务/仓储定期持久化到 MySQL。
- **Redis 非必需**：`appsettings` 中的 `Redis` 段与 `MatchEngine:BaseUrl` 为历史/重构方向保留配置（`docs/MATCHENGINE_HTTP_REFACTOR.md`），当前默认运行不依赖独立撮合服务或 Redis。

## 相关文档

- [AI_INTEGRATION_DESIGN.md](docs/AI_INTEGRATION_DESIGN.md) —— AI 集成设计（已实现）
- [ARCHITECTURE.md](docs/ARCHITECTURE.md) —— 整体架构
- [FRONTEND_BACKEND_API_MAPPING.md](docs/FRONTEND_BACKEND_API_MAPPING.md) —— 前后端接口映射
- [fix-password-hash-error.md](docs/fix-password-hash-error.md) —— Users 表缺 Email/PasswordHash 的修复步骤
- [MATCHENGINE_HTTP_REFACTOR.md](docs/MATCHENGINE_HTTP_REFACTOR.md) —— 撮合引擎 HTTP 化重构方向（未启用）
- [PROJECT_SELF_OPTIMIZATION_PLAN.md](docs/PROJECT_SELF_OPTIMIZATION_PLAN.md) —— 项目自优化计划

## 常见问题

1. **登录提示“用户名或密码错误”**：种子用户无密码哈希，先执行 `scripts/fix-user-table.sql`（见上）。
2. **AI 无响应/报错**：确认 Ollama 已启动且模型已 pull；或检查 `Ai:BaseUrl`/`ApiKey`；连续失败会触发熔断，等待 60 秒冷却自动恢复。
3. **没有实时行情**：检查网络能否访问 OKX WebSocket；行情兜底走 Binance REST，可配置代理 `Binance:ProxyUrl`。
4. **前端 502 / 连接失败**：核对前端 `proxy` 与 API 实际监听地址（5000 http vs 5001 https）。
5. **端口冲突**：用 `dotnet run --project src/CryptoSpot.API --urls http://localhost:5000` 指定端口。

## 免责声明

本项目仅用于技术学习与功能演示：行情来自公开数据源，撮合与资金均为模拟；AI 生成内容不构成投资建议。请勿用于真实交易。