# CryptoSpot - 数字资产现货交易演示项目

> 基于 .NET 9 + React 的现货交易平台学习项目，仅供学习参考。

## 项目简介

CryptoSpot 是一个功能完整的数字资产现货交易平台演示项目，包含用户认证、交易撮合、行情推送等核心功能。

### 主要特性

- 🔐 **用户系统**: JWT 认证、注册登录、资产管理
- 💹 **交易功能**: 限价单/市价单、订单管理、实时撮合
- 📊 **行情数据**: K线图表、实时价格、订单簿深度
- 📡 **实时推送**: SignalR 实时数据推送
- 🔴 **Redis-First**: 高性能内存撮合引擎
- 🤖 **做市系统**: 自动挂单、流动性支持

## 技术栈

### 后端 (.NET 9)
- ASP.NET Core Web API
- Entity Framework Core 9.0 + MySQL
- Redis (订单簿、撮合引擎)
- SignalR (实时推送)
- Clean Architecture (领域驱动设计)

### 前端 (React 18)
- React + TypeScript
- React Query (数据管理)
- Recharts (图表)
- SignalR Client (实时数据)

## 快速开始

### 环境要求
- .NET 9 SDK
- MySQL 8.x
- Redis
- Node.js 18+

### 后端启动

1. 创建数据库
```sql
CREATE DATABASE CryptoSpotDb CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
```

2. 配置连接字符串 (`src/CryptoSpot.API/appsettings.json`)
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "server=localhost;database=CryptoSpotDb;user=root;password=your_password"
  }
}
```

3. 启动项目
```bash
dotnet build CryptoSpot.sln
dotnet run --project src/CryptoSpot.API/CryptoSpot.API.csproj
```

4. 访问 Swagger: `https://localhost:5001/swagger`

### 前端启动

```bash
cd frontend
npm install
npm start
```

访问: `http://localhost:3000`

## 项目结构

```
CryptoSpot.sln
├── src/
│   ├── CryptoSpot.API/              # Web API 层
│   ├── CryptoSpot.Domain/           # 领域模型
│   ├── CryptoSpot.Application/      # 应用层
│   ├── CryptoSpot.Infrastructure/   # 基础设施
│   ├── CryptoSpot.Persistence/      # 数据持久化
│   ├── CryptoSpot.Bus/              # 命令总线
│   └── CryptoSpot.Redis/            # Redis 封装
├── frontend/                         # React 前端
└── scripts/                          # 数据库脚本
```

## 主要功能

### 交易功能
- 限价单/市价单下单
- 实时订单撮合
- 订单管理（查询、撤单）
- 成交历史

### 行情数据
- 多周期 K 线 (1m/5m/15m/30m/1h/4h/1d)
- 实时价格推送
- 订单簿深度
- 24h 行情统计

### 资产管理
- 可用余额/冻结余额
- 资产变动记录
- 实时余额推送

## API 文档

启动后端后访问 Swagger 文档：`https://localhost:5001/swagger`

### 主要接口

**认证**
- `POST /api/auth/register` - 注册
- `POST /api/auth/login` - 登录
- `GET /api/auth/me` - 获取当前用户

**交易**
- `GET /api/trading/pairs` - 获取交易对列表
- `POST /api/trading/orders` - 下单
- `DELETE /api/trading/orders/{orderId}` - 撤单
- `GET /api/trading/orders` - 查询订单
- `GET /api/trading/assets` - 查询资产

**行情**
- `GET /api/trading/klines/{symbol}` - 获取 K 线
- `GET /api/market/ticker/{symbol}` - 获取行情
- `GET /api/trading/orderbook/{symbol}` - 获取订单簿

**实时推送 (SignalR)**
- `/tradingHub` - 订阅实时数据
  - OrderUpdate - 订单更新
  - PriceUpdate - 价格更新
  - OrderBookUpdate - 订单簿更新
  - AssetUpdate - 资产更新

## 配置说明

### 数据库配置
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "server=localhost;database=CryptoSpotDb;user=root;password=your_password"
  }
}
```

### Redis 配置
```json
{
  "Redis": {
    "Configuration": "localhost:6379",
    "InstanceName": "CryptoSpot:"
  }
}
```

### JWT 配置
```json
{
  "JwtSettings": {
    "SecretKey": "your-secret-key-min-32-chars",
    "Issuer": "CryptoSpot",
    "Audience": "CryptoSpotUsers",
    "ExpirationDays": 7
  }
}
```

## 开发说明

### 架构设计
- **Clean Architecture**: 领域驱动设计，分层清晰
- **Repository Pattern**: 数据访问抽象
- **CQRS**: 命令查询职责分离
- **Event-Driven**: 基于事件的实时推送

### 数据流
1. 用户下单 → Redis 撮合引擎
2. 撮合成功 → 更新 Redis 订单簿和资产
3. 异步同步 → MySQL 持久化
4. SignalR 推送 → 实时通知客户端

## 注意事项

- ⚠️ 本项目仅供学习使用，不建议用于生产环境
- ⚠️ 首次启动会自动初始化数据库和测试数据
- ⚠️ 系统账号密码在代码中，实际项目需要加密存储

## License

MIT License - 仅供学习参考

## 联系方式

有问题欢迎提 Issue 或 Pull Request
