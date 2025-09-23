# CryptoSpot 服务架构设计

## 问题分析

### 当前问题
1. **重复服务**：KLineDataService、OrderService、TradeService、PriceDataService 在多个层级重复
2. **命名混乱**：TradingService vs TradeService，RefactoredOrderService vs OrderService
3. **职责不清**：Application层有太多具体实现，Infrastructure层有重复服务
4. **依赖混乱**：层级引用关系不明确

## 新的Clean Architecture分层

### 1. Domain层 (CryptoSpot.Core)
```
- Entities (实体)
  - Order, Trade, User, Asset, TradingPair, KLineData
- ValueObjects (值对象)
  - Price, Quantity, Money
- Domain Events (领域事件) - 已统一使用CryptoSpot.Bus处理
- Domain Service Interfaces (领域服务接口)
  - IOrderMatchingDomainService
  - ITradingDomainService
  - IUserDomainService
```

### 2. Application层 (CryptoSpot.Application)
```
- Application Services (应用服务) - 协调用例
  - TradingApplicationService - 协调交易相关用例
  - UserApplicationService - 协调用户相关用例
  - MarketDataApplicationService - 协调市场数据用例
- Command Handlers (命令处理器)
  - SubmitOrderCommandHandler
  - CancelOrderCommandHandler
  - UpdatePriceCommandHandler
- Event Handlers (事件处理器)
  - TradingEventHandler
- Use Cases (用例)
  - 每个应用服务方法代表一个用例
```

### 3. Infrastructure层 (CryptoSpot.Infrastructure)
```
- Repository Implementations (仓储实现)
  - OrderRepository, TradeRepository, UserRepository, etc.
- External Service Implementations (外部服务实现)
  - BinanceMarketDataProvider
- Infrastructure Services (基础设施服务)
  - DatabaseCoordinator - 数据库连接管理
  - CacheService - 缓存服务
  - UnitOfWork - 工作单元模式
```

### 4. API层 (CryptoSpot.API)
```
- Controllers (控制器)
  - TradingController, UserController, MarketDataController
- SignalR Hubs
  - TradingHub
- Background Services (后台服务)
  - MarketDataSyncService - 市场数据同步
  - OrderBookPushService - 订单簿推送
  - SignalRDataPushService - SignalR数据推送
```

## 服务职责划分

### Application Services (应用服务)
- **职责**：协调用例，编排业务流程，不包含具体业务逻辑
- **特点**：调用领域服务和基础设施服务，处理事务管理
- **示例**：
  - `TradingApplicationService.SubmitOrderAsync()` - 协调订单提交用例
  - `UserApplicationService.RegisterUserAsync()` - 协调用户注册用例
  - `MarketDataApplicationService.UpdateTradingPairPriceAsync()` - 协调价格更新用例

### Domain Services (领域服务)
- **职责**：包含核心业务逻辑，领域规则
- **特点**：纯业务逻辑，不依赖外部服务
- **示例**：
  - `OrderMatchingEngine` - 订单撮合核心逻辑
  - `TradingDomainService` - 交易领域规则

### Infrastructure Services (基础设施服务)
- **职责**：数据访问，外部服务调用，技术实现
- **特点**：处理技术细节，不包含业务逻辑
- **示例**：
  - `DatabaseCoordinator` - 数据库连接管理
  - `CacheService` - 缓存操作
  - `BinanceMarketDataProvider` - 外部API调用

## 依赖关系规则

### 依赖方向
```
API层 → Application层 → Domain层
  ↓         ↓
Infrastructure层 ← Domain层
```

### 具体规则
1. **Domain层**：不依赖任何其他层
2. **Application层**：只依赖Domain层
3. **Infrastructure层**：依赖Domain层，实现Application层接口
4. **API层**：依赖Application层和Infrastructure层

### 服务调用链
```
Controller → ApplicationService → DomainService → Repository
    ↓              ↓                    ↓
BackgroundService → ApplicationService → InfrastructureService
```

## 重构步骤

### 第一阶段：清理重复服务
1. 删除Application层的重复服务
2. 保留Infrastructure层的具体实现
3. 统一命名规范

### 第二阶段：创建应用服务
1. 创建TradingApplicationService
2. 创建UserApplicationService  
3. 创建MarketDataApplicationService

### 第三阶段：更新依赖注入
1. 更新ServiceCollectionExtensions
2. 移除重复注册
3. 明确服务职责

### 第四阶段：更新调用方
1. 更新Controller调用
2. 更新BackgroundService调用
3. 更新CommandHandler调用

## 命名规范

### 服务命名
- **Application Services**：`{Domain}ApplicationService`
  - `TradingApplicationService`
  - `UserApplicationService`
  - `MarketDataApplicationService`

- **Domain Services**：`{Domain}DomainService` 或 `{Function}Engine`
  - `OrderMatchingEngine`
  - `TradingDomainService`

- **Infrastructure Services**：`{Technology}{Function}Service`
  - `DatabaseCoordinator`
  - `CacheService`
  - `BinanceMarketDataProvider`

### 方法命名
- **Application Services**：`{Action}Async`
  - `SubmitOrderAsync`
  - `RegisterUserAsync`
  - `UpdatePriceAsync`

## 优势

1. **职责清晰**：每个服务有明确的职责边界
2. **依赖明确**：层级依赖关系清晰
3. **易于测试**：可以独立测试每个层级
4. **易于维护**：修改某个层级不影响其他层级
5. **易于扩展**：可以轻松添加新的应用服务或领域服务
