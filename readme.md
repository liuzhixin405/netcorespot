# CryptoSpot (学习用数字资产现货撮合 & 行情演示项目)

> 仅供个人学习 / 架构练习，功能仍在演进中，不建议用于生产。欢迎 Fork 交流。

## 1. 项目概要
一个演示型的数字资产现货交易平台，包括：
- 用户注册 / 登录 / 鉴权 (JWT)
- 交易对管理与基础行情数据
- 下单（限价 / 市价）、撤单、订单状态 & 历史查询
- 简化的撮合 / 成交记录逻辑（逐步重构中）
- K 线数据获取（数据库拉取 + 后台同步 + 行情流转发）
- 实时推送：基于 SignalR（后续可扩展 WebSocket 集群 / Redis 背板）
- 做市 / 自动交易（系统内置账户 + 自动挂单逻辑雏形）

## 2. 技术栈
Backend (.NET 8 / C#)：
- ASP.NET Core Web API + Minimal Hosting
- Entity Framework Core + MySQL (连接池配置)
- 分层 + Clean Architecture 风格 (Domain / Application / Infrastructure / Persistence / API)
- 依赖注入、配置化启动
- JWT 认证授权
- SignalR 实时数据推送
- Redis (缓存 / 未来可扩展深度、撮合快照、推送背板)
- 后台 HostedService：行情同步 / 缓存初始化 / 自动交易

Frontend (React + TypeScript)：
- React 18 + react-router-dom
- 状态 & 数据请求：react-query、上下文 AuthContext
- 图表：Recharts 展示基础 K 线 / 行情
- gRPC-Web（已生成类型文件）+ REST API 混合调用
- SignalR 订阅实时数据（订单、价格、行情）

Dev / 其他：
- 日志：Console / Debug（可扩展到 Serilog）
- 代码组织：领域实体、仓储接口、用例分离

## 3. 目录结构（摘取）
```
CryptoSpot.sln
frontend/               前端 React 工程
src/
  CryptoSpot.API/       API 层 (Controllers / Program / Hubs)
  CryptoSpot.Domain/    领域模型 (Entities / ValueObjects)
  CryptoSpot.Application/ 用例 & 抽象 (Abstractions, UseCases, DTOs)
  CryptoSpot.Infrastructure/ 外部实现 (Services, Repositories 实现, ExternalServices)
  CryptoSpot.Persistence/ 数据访问 (DbContext, Migrations, Repository 实现抽离)
  CryptoSpot.Redis/     Redis 封装（序列化、连接池、Cache Service）
```
(结构随演进可能微调)

## 4. 核心功能点
- 用户体系：注册 / 登录 / Token 刷新（后续可加 Refresh Token）
- 交易：提交 / 查询 / 撤销订单，订单状态跟踪（Pending / Active / PartiallyFilled / Filled / Cancelled）
- 资产：账户资产、可用 / 冻结拆分
- 行情：K 线、最新价、24h 统计（高 / 低 / 成交量 / 涨跌幅）
- 做市：系统账号初始化大额资产，供自动策略挂单（策略仍在扩展）
- 实时：通过 SignalR 推送订单更新 / 市场价格（可扩展 orderBook 增量）

## 5. 后端启动 & 本地运行
环境要求：
- .NET 8 SDK
- MySQL 8.x（或兼容版本），确保字符串匹配 `appsettings.json` 中连接串
- Redis （可选，若未启用可在配置中关闭相关功能）

步骤：
1. 创建数据库（若未自动创建）
```
CREATE DATABASE CryptoSpotDb CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
```
2. 调整连接串：`src/CryptoSpot.API/appsettings.json` -> ConnectionStrings:DefaultConnection
3. 运行构建：
```
dotnet build CryptoSpot.sln
```
4. 启动 API：
```
dotnet run --project src/CryptoSpot.API/CryptoSpot.API.csproj
```
5. 浏览 Swagger: `https://localhost:5001/swagger` (或控制台输出端口)
6. 首次启动会自动：
   - 初始化交易对 (BTCUSDT / ETHUSDT / SOLUSDT)
   - 创建系统用户 (SystemMarketMaker / SystemAdmin)
   - 注入做市资产

## 6. 前端运行
进入 `frontend/`：
```
npm install
npm start
```
默认开发地址：`http://localhost:3000` （已在后端 CORS 配置中允许）

前端通过：
- `/api/auth/*` 进行鉴权
- `/api/trading/*` 获取交易和资产数据
- SignalR Hub: `/tradingHub`

## 7. 主要 API 路径（节选）
Auth:
- POST /api/auth/register
- POST /api/auth/login
- GET  /api/auth/me (需要 JWT)
- POST /api/auth/logout

Trading:
- GET  /api/trading/pairs
- GET  /api/trading/pairs/{symbol}
- GET  /api/trading/klines/{symbol}?interval=1h&limit=100
- GET  /api/trading/assets
- GET  /api/trading/orders
- GET  /api/trading/open-orders
- GET  /api/trading/order-history
- GET  /api/trading/trades
- POST /api/trading/orders
- DELETE /api/trading/orders/{orderId}

## 8. 配置说明
`appsettings.json` 关键段：
- ConnectionStrings.DefaultConnection : MySQL 连接
- JwtSettings : 签名键 / Issuer / Audience / 过期天数
- Binance.ProxyUrl : 行情抓取时的代理（可选）

如需关闭 HTTPS 强制，可在 Program 中调整 `UseHttpsRedirection`。
