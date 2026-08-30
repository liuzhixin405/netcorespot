# CryptoSpot 项目自身优化实施清单

> **定位**：本文档是 `AI_INTEGRATION_DESIGN.md` 中划出的"项目自身优化"部分的独立实施设计。
> 做市策略、风控检测、回测/信号引擎均为**确定性工程**，不依赖 LLM，可单测、可复现、毫秒级响应。
> 按优先级排序：**A（做市）→ B（风控）→ C（回测/信号）**，彼此独立，可并行开发。

---

## 目录

- [A. 动态做市策略（VolatilityAwareMarketMakingStrategy）](#a-动态做市策略)
- [B. 风控检测引擎（RiskMonitorService）](#b-风控检测引擎)
- [C. 回测与信号引擎（BacktestEngine + SignalEngine）](#c-回测与信号引擎)
- [附录：实施清单与优先级](#附录实施清单与优先级)

---

## A. 动态做市策略

### A.1 现状（已核实）

`src/CryptoSpot.Infrastructure/Services/AutoTradingLogicService.cs`

```csharp
// CreateMarketMakingOrdersAsync 中的核心逻辑（第 ~105 行起）
var buyPrice  = currentPrice.Price * 0.9995m;  // 固定低于市价 0.05%
var sellPrice = currentPrice.Price * 1.0005m;  // 固定高于市价 0.05%
var quantity  = (decimal)(Random.Shared.NextDouble() * 0.1 + 0.01); // 随机数量
```

**缺陷清单**：

| # | 缺陷 | 后果 |
|---|------|------|
| 1 | 价差固定 0.05% | 波动大时卖单瞬间成交（亏损风险），波动小时价差过大（无成交） |
| 2 | 数量随机 | 无法控制库存敞口，系统账户资产可能单向堆积 |
| 3 | 无库存感知 | 已持有大量 BTC 时仍对称报价，风险敞口无限 |
| 4 | 无订单簿感知 | 单边深度失衡时不调整报价方向 |
| 5 | 无决策审计 | 无法事后分析做市质量 |
| 6 | 每次循环重复下单 | 旧挂单未清理时订单堆积（已有 CancelExpiredSystemOrdersAsync 5 分钟清理，但循环内未调用） |

### A.2 目标设计

```
┌─────────────────────────────────────────────────────────┐
│               AutoTradingLogicService                    │
│   ExecuteTradingCycleAsync → CreateMarketMakingOrders    │
│                         │                                │
│                         ▼                                │
│          IMarketMakingStrategy.Decide(...)               │
│                         │                                │
│              ┌──────────┴──────────┐                     │
│              ▼                     ▼                     │
│  VolatilityAwareMarketMaking    降级回退                │
│  Strategy（确定性算法）        （异常→固定规则）          │
└─────────────────────────────────────────────────────────┘
```

### A.3 接口与实现

**新增接口**（放 `Application/Abstractions/Services/Trading/`）：

```csharp
public interface IMarketMakingStrategy
{
    MarketMakingDecision Decide(MarketMakingContext context);
}

public sealed class MarketMakingContext
{
    public required string Symbol { get; init; }
    public required decimal CurrentPrice { get; init; }
    public required IReadOnlyList<decimal> RecentClosePrices { get; init; } // 最近 N 根收盘价
    public required decimal CurrentInventory { get; init; }                 // 当前库存（以计价币计）
    public required decimal TargetInventory { get; init; }                  // 目标库存
    public decimal BidDepth { get; init; }                                  // 买盘深度
    public decimal AskDepth { get; init; }                                  // 卖盘深度
    public int RecentTradeCount { get; init; }                              // 近窗口成交笔数
}

public sealed record MarketMakingDecision
{
    public required decimal BuyPrice { get; init; }
    public required decimal SellPrice { get; init; }
    public required decimal Quantity { get; init; }
    public required string Rationale { get; init; }  // 人类可读决策理由（审计）
}
```

**核心算法**（`Infrastructure/Services/MarketMaking/VolatilityAwareMarketMakingStrategy.cs`）：

```
1. EWMA 波动率：σ² = α·(ln(Pₜ/Pₜ₋₁))² + (1−α)·σ²ₜ₋₁，σ = √σ²
2. 波动率价差因子：spreadMultiplier = 1 + λ·(σ/σ_ref − 1)，clamp 到 [0.5, 3.0]
3. 库存偏置：inventoryBias = β · (inventory − target) / target，clamp 到 [−maxBias, +maxBias]
4. 深度偏置：depthBias = γ · (bidDepth − askDepth) / (bidDepth + askDepth)，无深度数据时为 0
5. 净偏置：bias = inventoryBias + depthBias
6. 报价：
      buyPrice  = P × (1 − spread/2 + bias)
      sellPrice = P × (1 + spread/2 + bias)
7. 数量：quantity = clamp(baseQty × (1 + 成交频率调整), minQty, maxQty)
      成交频率高 → 放大数量（市场活跃），反之缩小
```

**参数配置**（可配置，默认值符合当前 0.05% 基准）：

| 参数 | 含义 | 默认值 |
|------|------|--------|
| `BaseSpread` | 基准价差（单边） | 0.0005（0.05%） |
| `Lambda` | 波动率敏感度 λ | 1.0 |
| `SigmaRef` | 参考波动率 σ_ref | 0.002 |
| `Beta` | 库存偏置系数 β | 0.3 |
| `Gamma` | 深度偏置系数 γ | 0.2 |
| `MaxBias` | 最大偏置（避免报价极端） | 0.002 |
| `MinOrderSize` / `MaxOrderSize` | 数量上下限 | 0.01 / 0.5 |
| `TargetInventoryUsdt` | 目标库存（计价币） | 1000 |

**降级路径**：`Decide` 抛异常 / 输入非法 → 捕获并回退到现固定规则（0.05% 对称），保证做市永不停摆。

### A.4 审计

新增 `MarketMakingDecisionLog` 实体 + 表：

| 字段 | 说明 |
|------|------|
| Id | 主键 |
| Symbol | 交易对 |
| ExecutedAt | 决策时间 |
| CurrentPrice / BuyPrice / SellPrice / Quantity | 决策输出 |
| Inventory / Sigma / Bias | 决策输入快照 |
| Rationale | 决策理由（含各因子贡献） |
| IsFallback | 是否走了降级路径 |

用途：事后统计实际成交价 vs 决策价差，评估策略有效性。

### A.5 与现有代码的集成点

| 修改点 | 说明 |
|--------|------|
| `AutoTradingLogicService.CreateMarketMakingOrdersAsync` | 用策略替换硬编码价差/数量 |
| 循环开头调用 `CancelExpiredSystemOrdersAsync` | 先清旧单再挂新单，避免堆积 |
| `MarketMakerOptions` | 扩展为完整策略配置（保留 UserIds） |
| `ITradingService.SubmitOrderAsync` | 不变，仍走撮合引擎 |

---

## B. 风控检测引擎

### B.1 现状（已核实）

- 仅 `User.MaxRiskRatio`（默认 0.1）字段存在，`TradingExtensionsDto.RiskWarnings` 只是 `Warnings` 别名
- **没有任何风控服务、检测器、风控事件实体**，属于从零建设

### B.2 目标设计

```
┌──────────────────────────────────────────────────────┐
│                  RiskMonitorService                   │
│              (BackgroundService, 默认每 5s 扫描)      │
│                          │                            │
│        ┌─────────────────┼─────────────────┐         │
│        ▼                 ▼                 ▼         │
│  AbnormalOrderDetector PriceDeviation    Inventory    │
│  （异常大单）           Detector（价格偏离） Concentration│
│        │                 │                 │         │
│        └─────────────────┼─────────────────┘         │
│                          ▼                           │
│                   RiskEvent 记录                     │
│                   + SignalR 推送                     │
│                   + 可配置动作（告警/冻结）           │
└──────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────┐
│              下单前拦截（IOrderRiskChecker）          │
│   SubmitOrderAsync 入口 → 规则校验 → 拒绝/放行        │
└──────────────────────────────────────────────────────┘
```

**两道防线**：
1. **前置拦截**：订单提交时同步校验（拒绝明显违规单）
2. **后台检测**：扫描已发生数据（捕捉跨订单/跨时间的模式）

### B.3 数据模型

```csharp
public enum RiskEventType
{
    AbnormalOrder,        // 异常大单：单笔金额 > MaxRiskRatio × 账户资产
    PriceDeviation,       // 价格偏离：订单价偏离市价 > 阈值
    WashTrading,          // 对倒/自成交嫌疑：同一用户买卖交替高频成交
    OrderFlood,           // 订单洪泛：短窗口内提交订单数超阈值
    InventoryConcentration, // 库存集中：单币种持仓占比超阈值
    TradingSuspension     // 熔断：已触发风控，暂停下单
}

public enum RiskSeverity { Low, Medium, High, Critical }

public class RiskEvent : BaseEntity
{
    public long UserId { get; set; }
    public RiskEventType Type { get; set; }
    public RiskSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public long? OrderId { get; set; }
    public string MetaJson { get; set; } = "{}";   // 检测快照（价格/资产/阈值等）
    public DateTime DetectedAt { get; set; }
    public RiskEventStatus Status { get; set; }     // New / Acknowledged / Resolved
}
```

### B.4 检测器规则（确定性）

| 检测器 | 规则 | 默认阈值 |
|--------|------|----------|
| AbnormalOrderDetector | 单笔订单金额 > MaxRiskRatio × 账户总资产 | 0.1（读 User.MaxRiskRatio） |
| PriceDeviationDetector | 限价单价格偏离当前市价 > 阈值 | 5% |
| WashTradingDetector | 同用户 60s 内买卖成交 ≥ N 笔且无净持仓变化 | 5 笔 |
| OrderFloodDetector | 60s 内提交订单 ≥ N 笔 | 20 笔 |
| InventoryConcentrationDetector | 单币种市值 / 总资产 > 阈值 | 0.8 |

**熔断状态机**（`TradingSuspension`）：

```
检测到 Critical 事件 → 用户进入 Suspend 状态（下单被拒） → 冷却期 10 分钟
冷却期结束 → 自动恢复；冷却期内再次触发 → 冷却期翻倍（上限 60 分钟）
```

### B.5 集成点

| 修改点 | 说明 |
|--------|------|
| `OrderService.SubmitOrderAsync` | 入口调用 `IOrderRiskChecker.CheckAsync`，拒绝时返回错误码 |
| `TradeService` / 成交记录 | 后台检测器扫描数据源 |
| SignalR Hub | 新增 `RiskAlert` 事件（前端已有风控仪表盘时对接） |
| 管理 API | `GET /api/risk/events`、`POST /api/risk/events/{id}/ack` |

---

## C. 回测与信号引擎

### C.1 现状（已核实）

- `KLineDataService.GetKLineDataAsync(symbol, interval, startTime, endTime, limit)` **已支持按时间范围查询**，数据源可用
- `KLineData` 实体：OHLCV 完整（Open/High/Low/Close/Volume + OpenTime/CloseTime）
- `Trade` 实体：Price/Quantity/Fee/ExecutedAt 完整
- **没有测试项目**（解决方案仅有 src 下 5 个 csproj）——需要新增

### C.2 架构

```
┌──────────────────────────────────────────────────────┐
│            TechnicalIndicators（纯函数静态类）        │
│   Ema / Rsi / Bollinger / Macd / StdDev / Atr        │
│         ▲                     ▲                      │
│         │                     │                      │
│  ┌──────┴──────┐      ┌──────┴──────┐               │
│  │ SignalEngine │      │ BacktestEngine│             │
│  │ 实时K线→信号  │      │ 历史K线→模拟成交 │            │
│  └──────┬──────┘      └──────┬──────┘               │
│         │                     │                      │
│         ▼                     ▼                      │
│   Signal(买入/卖出/观望)  BacktestResult(统计+权益曲线)│
└──────────────────────────────────────────────────────┘
```

**关键设计**：
- 指标计算与策略逻辑完全分离，指标层是纯函数 → 可单测
- 回测与信号共用同一套策略规则 → 回测有效则实时信号行为一致

### C.3 指标层（纯函数）

```csharp
public static class TechnicalIndicators
{
    // 简单移动平均
    public static IReadOnlyList<decimal> Sma(IReadOnlyList<decimal> values, int period);
    // 指数移动平均（使用 α = 2/(period+1)）
    public static IReadOnlyList<decimal> Ema(IReadOnlyList<decimal> values, int period);
    // 相对强弱指标（Wilder 平滑）
    public static IReadOnlyList<decimal> Rsi(IReadOnlyList<decimal> values, int period);
    // 布林带：返回 (Mid, Upper, Lower) 三个序列
    public static (IReadOnlyList<decimal> Mid, IReadOnlyList<decimal> Upper, IReadOnlyList<decimal> Lower)
        Bollinger(IReadOnlyList<decimal> values, int period, decimal k);
    // MACD：返回 (DIF, DEA, Histogram) 三个序列
    public static (IReadOnlyList<decimal> Dif, IReadOnlyList<decimal> Dea, IReadOnlyList<decimal> Hist)
        Macd(IReadOnlyList<decimal> values, int fast, int slow, int signal);
    // 滚动标准差（含样本校正）
    public static IReadOnlyList<decimal> StdDev(IReadOnlyList<decimal> values, int period);
}
```

### C.4 策略层

```csharp
public enum StrategyType { MaCross, RsiMeanReversion, BollingerBreak, MacdCross }

public sealed record BacktestConfig
{
    public required string Symbol { get; init; }
    public required string Interval { get; init; }   // 如 "1m" / "5m" / "1h"
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public required StrategyType Strategy { get; init; }
    public required IDictionary<string, decimal> Parameters { get; init; } // 如 {"FastPeriod":5,"SlowPeriod":20}
    public decimal InitialCapital { get; init; } = 1000m;
    public decimal FeeRate { get; init; } = 0.001m;  // 0.1% 手续费
}

public sealed record Signal
{
    public required DateTime Time { get; init; }
    public required SignalAction Action { get; init; }  // Buy / Sell / Neutral
    public required decimal Price { get; init; }
    public required string Reason { get; init; }        // 触发的规则描述
}
```

**策略规则定义**（与回测/信号共用）：

| 策略 | 买入信号 | 卖出信号 |
|------|----------|----------|
| MaCross | 快线上穿慢线 | 快线下穿慢线 |
| RsiMeanReversion | RSI(14) < 30（超卖） | RSI(14) > 70（超买） |
| BollingerBreak | 收盘突破下轨 | 收盘突破上轨 |
| MacdCross | DIF 上穿 DEA | DIF 下穿 DEA |

### C.5 回测引擎（避免前视偏差）

```
模拟撮合规则：
1. 以 K 线收盘价计算信号
2. 信号在下一根 K 线开盘价成交（防止使用未来数据）
3. 手续费按成交额 × FeeRate 扣除
4. 持仓为全仓进出（简化第一版），后续可加仓位管理
```

**输出** `BacktestResult`：

| 指标 | 计算方式 |
|------|----------|
| TotalReturn | 期末权益 / 初始资金 − 1 |
| MaxDrawdown | 权益曲线最大回撤（峰值到谷值） |
| WinRate | 盈利交易数 / 总交易数 |
| TradeCount | 总交易次数（买卖各记一次） |
| SharpeRatio | 每期收益率均值 / 标准差 × √(年化频率)，无风险利率取 0 |
| EquityCurve | 每根 K 线收盘权益序列（前端画图用） |

### C.6 信号引擎

- 由 K 线服务驱动（新 K 线到达时计算最新信号）或定时轮询
- 输出 `Signal` 推送到 SignalR（`SignalUpdate` 事件）与数据库（`SignalHistory` 表）
- 与回测共用同一策略实现，保证一致性

### C.7 新增 API

```
POST /api/backtest           — 提交回测请求（BacktestConfig）→ 同步返回 BacktestResult
GET  /api/backtest/strategies — 支持的策略枚举 + 参数说明（供前端下拉）
GET  /api/signals/{symbol}   — 某交易对最近信号
GET  /api/signals/{symbol}/history — 信号历史
```

---

## 附录：实施清单与优先级

### 优先级排序依据

- **A（做市）**：直接影响系统流动性与做市质量，改动集中在单个服务，收益最直接 → **P0**
- **B（风控）**：保护用户与系统资产，从零建设但规则清晰 → **P1**
- **C（回测/信号）**：价值高但依赖前端配合，纯增量 → **P2**

### 实施步骤

**A. 动态做市策略（P0）**

- [ ] A-1 新增 `IMarketMakingStrategy` / `MarketMakingContext` / `MarketMakingDecision`（Application 层）
- [ ] A-2 实现 `VolatilityAwareMarketMakingStrategy`（EWMA 波动率 + 库存 + 深度 + 成交频率）
- [ ] A-3 扩展 `MarketMakerOptions` 为完整策略配置（保留 UserIds）
- [ ] A-4 改造 `AutoTradingLogicService`：调用策略 + 异常降级回固定规则
- [ ] A-5 循环开头调用 `CancelExpiredSystemOrdersAsync` 清理旧单
- [ ] A-6 新增 `MarketMakingDecisionLog` 实体 + 仓储 + 审计写入
- [ ] A-7 单测：EWMA 计算、偏置 clamp、极端输入降级（≥ 8 个用例）

**B. 风控检测引擎（P1）**

- [ ] B-1 新增 `RiskEventType` / `RiskSeverity` / `RiskEvent` 实体（含 Status 状态机）
- [ ] B-2 新增 `IOrderRiskChecker` 前置拦截 + `AbnormalOrder` / `PriceDeviation` 规则
- [ ] B-3 新增 `RiskMonitorService`（BackgroundService，5s 扫描）+ 5 个检测器
- [ ] B-4 熔断状态机（Suspend → 冷却期 → 指数退避恢复）
- [ ] B-5 SignalR `RiskAlert` 推送 + 管理 API（查询/确认/解决）
- [ ] B-6 单测：各检测器规则边界、状态机转换（≥ 10 个用例）

**C. 回测与信号引擎（P2）**

- [ ] C-1 新增测试项目 `tests/CryptoSpot.Tests`（xUnit，首次建立测试基建）
- [ ] C-2 实现 `TechnicalIndicators` 纯函数 + 指标单测（EMA/RSI/布林/MACD 已知数据对比）
- [ ] C-3 实现 4 种策略规则（与信号共用）
- [ ] C-4 实现 `BacktestEngine`（避免前视偏差 + 手续费）+ 绩效统计
- [ ] C-5 实现 `SignalEngine` + SignalR 推送
- [ ] C-6 新增回测/信号 API + 前端页面（可选）

### 验收标准

- A/B/C 全部**不依赖 LLM**，离线可运行、可单测
- A：做市订单价差随波动率动态变化，库存偏离被校正，决策可审计
- B：恶意订单被拦截，风控事件可追溯、可恢复
- C：回测结果与手动计算的已知样例一致（指标层）、回测与实时信号策略行为一致