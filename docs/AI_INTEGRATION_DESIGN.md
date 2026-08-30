# CryptoSpot AI 集成设计文档

> **版本**：v1.0（等待实现）
> **目标**：为 CryptoSpot 现货交易平台接入真实 LLM（支持本地部署），提供实质性的 AI 实用功能
> **LLM 方案**：本地部署为主（Ollama / LM Studio / vLLM），通过 OpenAI 兼容 API 统一接入
> **状态**：🟡 设计完成，待开发

---

## 目录

1. [背景与目标](#1-背景与目标)
2. [总体架构](#2-总体架构)
3. [LLM 接入层设计](#3-llm-接入层设计)
4. [一期：AI 智能行情分析师](#4-一期ai-智能行情分析师)
5. [一期：AI 交易助手（自然语言下单）](#5-一期ai-交易助手自然语言下单)
6. [二期：风控监控系统（确定性引擎 + AI 洞察）](#6-二期风控监控系统拆分确定性检测引擎--ai-洞察层)
7. [三期：动态做市策略（项目自身优化，非 AI）](#7-三期项目自身优化动态做市策略确定性算法不引入-ai)
8. [四期：回测与信号（确定性引擎 + AI 解析）](#8-四期交易策略回测与信号拆分确定性引擎--ai-策略描述解析)
9. [五期（N期）：AI 平台助手与更多](#9-五期n期ai-平台助手与更多)
10. [数据模型](#10-数据模型)
11. [API 设计](#11-api-设计)
12. [前端设计](#12-前端设计)
13. [配置设计](#13-配置设计)
14. [测试计划](#14-测试计划)
15. [风险与边界](#15-风险与边界)

---

## 1. 背景与目标

### 1.1 现状

CryptoSpot 是一个功能完整的现货交易平台，具备：
- JWT 认证、限价/市价下单、内存撮合引擎（价格优先/时间优先）
- Binance REST + OKX WebSocket 实时行情
- SignalR 实时推送、K 线图表、订单簿深度
- 自动做市商机器人（**硬编码规则**：固定 0.05% 价差 + 随机数量）
- 用户类型定义了 `RiskManagement` 角色（**无实现，空壳**）

当前项目**零 AI 代码**，所有功能均为确定性规则。

### 1.2 AI 能带来的实质价值

| 价值点 | 现状痛点 | AI 解决方式 |
|--------|----------|-------------|
| 行情分析 | 用户需自己看图表判断 | 自然语言问答，AI 读取 K 线/深度/资产生成分析 |
| 交易操作 | 需手动填表单下单 | 自然语言下单 + Agentic 工作流 + 风控审批 |
| 风控 | RiskManagement 角色空壳 | AI 异常检测 + 实时告警 + 熔断建议 |
| 做市 | 固定价差、随机数量 | **项目自身优化**：确定性量化算法动态调整（非 AI） |
| 策略 | 无回测能力 | 回测引擎 = 项目自身优化；AI 仅解析自然语言策略描述 |

### 1.3 设计原则（沿用已验证的模式）

1. **AI 只做理解和编排**：AI 生成意图、计划、分析结论，**不直接碰数据库和撮合引擎**
2. **业务写操作走确定性服务**：所有下单/撤单走现有 `ITradingService` / `IOrderService`
3. **风控拦截**：高风险操作必须经过 `Guardrail` 校验 + 人工确认
4. **全链路审计**：AI 的每次调用、每个决策、每个操作都可追踪、可回放
5. **本地 LLM 优先**：通过 OpenAI 兼容接口，本地 Ollama/LM Studio 与云端 DeepSeek/OpenAI 无缝切换
6. **零侵入**：AI 层作为独立新模块叠加，不修改现有撮合引擎、交易核心逻辑

### 1.4 核心划分：AI 集成 vs 项目自身优化

**判断标准**：如果某个能力可以用确定性算法/规则实现，且确定性实现更快、更稳、更可解释，它就不是"AI 功能"，而是"项目自身优化"，不需要引入 LLM。

**AI 集成只保留"离开 LLM 就无法实现"的能力**：
- 自然语言理解（听懂用户问题/指令）
- 意图解析与指令 → 结构化参数
- 生成式文本输出（分析报告、操作建议、风险解释）
- 对话式交互与多轮确认
- 语义检索（RAG 问答）

**本项目五期规划中"AI 做市商策略优化"等项属于项目自身优化**：用确定性量化算法即可实现（响应更快、可解释、可单测），应从 AI 规划中拆分出去。

**独立实施文档**：做市/风控/回测三块的具体设计（接口、算法、数据模型、实施清单、验收标准）见 [PROJECT_SELF_OPTIMIZATION_PLAN.md](./PROJECT_SELF_OPTIMIZATION_PLAN.md)，本文档不再重复。

| 类别 | 判断 | 归属 |
|------|------|------|
| 行情分析师 / 交易助手 / 客服 / 情感分析 / 投资报告 / 策略描述解析 | 离开 LLM 无法实现 | **AI 集成** |
| 做市策略优化 / 风控检测 / 回测引擎 / 信号引擎 | 确定性算法即可 | **项目自身优化** |

---

## 2. 总体架构

```
┌─────────────────────────────────────────────────────────────────┐
│                        Frontend (React 18)                       │
│  ┌─────────────┐  ┌──────────────┐  ┌─────────────────────────┐ │
│  │ 交易面板     │  │ K线/订单簿    │  │ AI 助手面板 (新增)       │ │
│  └─────────────┘  └──────────────┘  └─────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                              │ HTTP + SignalR
┌─────────────────────────────────────────────────────────────────┐
│                    CryptoSpot.API (Controllers)                  │
│  ┌──────────────────┐  ┌──────────────────────────────────────┐ │
│  │ 现有 Trading/     │  │ AI 控制器 (新增)                      │ │
│  │ Auth/KLine 等     │  │  /api/ai/chat  /api/ai/analyze       │ │
│  │                   │  │  /api/ai/trade /api/ai/stream        │ │
│  └──────────────────┘  └──────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                              │
┌─────────────────────────────────────────────────────────────────┐
│                 CryptoSpot.Application (新增 AI 抽象)            │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │ IAiModel            — LLM 调用抽象（Chat/ChatStream）       │ │
│  │ IAiToolRegistry     — AI 工具注册表                        │ │
│  │ IAiConversationStore— 会话存储                            │ │
│  │ IAiGuardrailPolicy  — AI 风控策略                         │ │
│  │ IAiApprovalService  — 高风险操作审批                       │ │
│  │ IAiAuditLog         — 审计日志                            │ │
│  │ IAiAnalyzer         — 行情分析服务                        │ │
│  │ IAiTradeService     — 交易助手服务                        │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                              │
┌─────────────────────────────────────────────────────────────────┐
│        CryptoSpot.Infrastructure (新增 AI 实现)                  │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │ LLM 客户端（OpenAI 兼容）→ Ollama / LM Studio / DeepSeek   │ │
│  │ AiTool 实现（只读：查K线/查深度/查资产/查订单）             │ │
│  │ AiAgentOrchestrator — Agentic 循环（ReAct）                │ │
│  │ AiGuardrail — 金额/价格/频率/自成交拦截                     │ │
│  │ AiApproval — 内存/持久化审批                                │ │
│  │ AiAudit — 结构化审计日志                                    │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                              │
┌─────────────────────────────────────────────────────────────────┐
│           现有交易核心（不修改）                                  │
│   ITradingService / IOrderService / 撮合引擎 / MarketDataProvider │
└─────────────────────────────────────────────────────────────────┘
```

---

## 3. LLM 接入层设计

### 3.1 抽象接口

```csharp
// CryptoSpot.Application/Abstractions/Services/Ai/IAiModel.cs
public interface IAiModel
{
    /// <summary>模型标识（如 "ollama/qwen2.5-coder:32b"）</summary>
    string ModelId { get; }

    /// <summary>非流式对话（用于简单分析）</summary>
    Task<AiChatResponse> ChatAsync(
        AiChatRequest request,
        CancellationToken ct = default);

    /// <summary>流式对话（用于实时输出）</summary>
    IAsyncEnumerable<AiChatChunk> ChatStreamAsync(
        AiChatRequest request,
        CancellationToken ct = default);
}

public sealed record AiChatRequest
{
    public required IReadOnlyList<AiMessage> Messages { get; init; }
    public IReadOnlyList<AiToolDefinition>? Tools { get; init; }
    public double Temperature { get; init; } = 0.2;
    public int MaxTokens { get; init; } = 2048;
}

public sealed record AiMessage
{
    public required string Role { get; init; }   // system / user / assistant / tool
    public required string Content { get; init; }
    public string? ToolCallId { get; init; }
    public IReadOnlyList<AiToolCall>? ToolCalls { get; init; }
}

public sealed record AiToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string ParametersJsonSchema { get; init; }  // JSON Schema
}

public sealed record AiToolCall
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ArgumentsJson { get; init; }
}

public sealed record AiChatResponse
{
    public required string Content { get; init; }
    public IReadOnlyList<AiToolCall>? ToolCalls { get; init; }
    public string? FinishReason { get; init; }
}

public sealed record AiChatChunk
{
    public string? ContentDelta { get; init; }
    public IReadOnlyList<AiToolCall>? ToolCalls { get; init; }
    public string? FinishReason { get; init; }
}
```

### 3.2 OpenAI 兼容实现（统一接入 Ollama / LM Studio / DeepSeek / OpenAI）

```csharp
// CryptoSpot.Infrastructure/Ai/OpenAiCompatAiModel.cs
public sealed class OpenAiCompatAiModel : IAiModel
{
    private readonly HttpClient _http;
    private readonly AiModelOptions _options;

    public async Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken ct)
    {
        // POST {BaseUrl}/chat/completions
        // 请求体：model, messages, tools, temperature, max_tokens, stream=false
        // 解析：choices[0].message.content / tool_calls
    }

    public async IAsyncEnumerable<AiChatChunk> ChatStreamAsync(...)
    {
        // POST {BaseUrl}/chat/completions, stream=true
        // 逐行解析 SSE：data: {...}
        // 累积 delta.content 和 delta.tool_calls[].function.arguments
    }
}
```

**关键实现要点（流式工具调用参数累积）**：
- OpenAI 流式 API 中，工具调用的 `arguments` 是分片返回的 JSON 字符串
- 必须按 `tool_calls[].index` 累积每个工具的 `id`、`name`、`arguments` 片段
- 直到流结束（`finish_reason == "tool_calls"`）才将累积的 arguments 作为完整 JSON 解析
- 参考项目历史经验：**不要对每个分片单独反序列化**，否则工具参数会丢失

### 3.3 本地 LLM 配置

```json
// appsettings.json 新增
{
  "Ai": {
    "Provider": "OpenAiCompat",
    "BaseUrl": "http://localhost:11434/v1",   // Ollama 默认
    "ApiKey": "ollama",                        // Ollama 任意值即可
    "Model": "qwen2.5-coder:32b",              // 推荐本地模型
    "ModelFast": "qwen2.5-coder:7b",           // 简单任务快速模型（可选）
    "Temperature": 0.2,
    "MaxTokens": 2048,
    "TimeoutSeconds": 120,
    "EnableStreaming": true
  }
}
```

**本地 LLM 部署选项对照表**：

| 方案 | 命令/配置 | 模型推荐 | 适用场景 |
|------|-----------|----------|----------|
| Ollama | `ollama pull qwen2.5-coder:32b` | qwen2.5-coder:32b / 7b | 开箱即用，OpenAI 兼容 `/v1` |
| LM Studio | 图形界面加载 GGUF，启动 Server | qwen2.5-coder-instruct | 桌面友好 |
| vLLM | `vllm serve ...` | qwen2.5-32b | 生产级吞吐 |
| DeepSeek API | `BaseUrl=https://api.deepseek.com` | deepseek-chat | 云端兜底 |

**工具调用能力要求**：所选模型必须支持 **Function Calling / Tool Use**。推荐：
- `qwen2.5-coder:32b`（支持 tool calling，中文好）
- `qwen2.5:32b`（通用，支持 tool calling）
- 不支持 tool calling 的模型只能用于一期"纯分析"（无工具），交易助手需要 tool calling

### 3.4 模型降级与容错

```
配置多个模型：Models: [ "qwen2.5-coder:32b", "qwen2.5-coder:7b", "deepseek-chat" ]
调用顺序：优先本地，失败/超时切换下一个
熔断：连续 3 次失败 → 冷却 60s
```

---

## 4. 一期：AI 智能行情分析师

### 4.1 功能

用户用自然语言询问行情，AI 读取真实数据生成分析：

```
用户：BTC 现在什么情况？我该加仓还是观望？
AI：  [调用工具获取 BTCUSDT 多周期K线、订单簿、24h行情、你的持仓]
      BTC 现价 67,240 USDT，24h 涨 2.1%。
      短期（1h）处于上升通道，但 4h 级别逼近阻力位 68,500。
      订单簿卖压集中在 68,000-68,500 区间。
      你当前持有 0.15 BTC，成本 65,100，浮盈 3.3%。
      建议：可持有观察，若 1h 收线站稳 68,500 上方可加仓，
      止损参考 66,200（跌破则离场）。风险提示：加密资产波动大，请谨慎。
```

### 4.2 工具（只读）

| 工具名 | 功能 | 底层服务 |
|--------|------|----------|
| `get_klines` | 获取 K 线 OHLCV | `IKLineDataService` / `ITradingService.GetKLineDataAsync` |
| `get_orderbook` | 订单簿深度 | `ITradingService.GetOrderBookDepthAsync` |
| `get_ticker` | 24h 行情 | `IMarketDataProvider` |
| `get_assets` | 用户资产 | `IAssetService` |
| `get_open_orders` | 用户挂单 | `ITradingService.GetOpenOrdersAsync` |
| `get_recent_trades` | 最近成交 | `ITradingService.GetMarketRecentTradesAsync` |

### 4.3 系统提示词（System Prompt）

```
你是 CryptoSpot 交易平台的 AI 行情分析师。
你只能基于工具返回的真实数据进行分析，禁止编造价格或数据。
回答结构：
1. 当前价格与关键数据
2. 技术面简要分析（趋势/支撑阻力/量能）
3. 用户持仓与盈亏（如涉及）
4. 操作建议（明确、可执行）
5. 风险提示
语气专业、客观。不确定时明确说明"数据不足，无法判断"。
```

### 4.4 实现组件

```csharp
// IAiAnalyzer
public interface IAiAnalyzer
{
    Task<AiChatResponse> AnalyzeAsync(
        long userId,
        string question,
        string? conversationId,
        CancellationToken ct = default);
}
```

- 无工具调用循环时直接返回分析
- 有工具调用时走 `AiAgentOrchestrator`（ReAct 循环，最多 N 轮）

---

## 5. 一期：AI 交易助手（自然语言下单）

### 5.1 功能

```
用户：帮我以市价买入 0.01 BTC
用户：BTC 跌到 65000 就帮我止损卖出
用户：把我的 BTC 挂单全部撤掉
```

### 5.2 Agentic 工作流（ReAct 循环）

```
自然语言输入
    ↓
LLM 解析意图 + 生成工具调用计划
    ↓
[循环，最多 5 轮]
    ├─ LLM 决定调用工具（查K线/查资产/下单/撤单）
    ├─ 执行工具（确定性服务）
    ├─ 工具结果回传 LLM
    └─ LLM 判断是否完成
    ↓
高风险操作 → Guardrail 拦截 → 人工确认
    ↓
执行 → 审计日志 → 返回结果
```

### 5.3 写操作工具（需风控）

| 工具名 | 功能 | 风控等级 |
|--------|------|----------|
| `place_order` | 下单（限价/市价） | 🟠 需校验，大额需审批 |
| `cancel_order` | 撤单 | 🟢 低风险 |
| `cancel_all_orders` | 批量撤单 | 🟡 需确认 |

### 5.4 风控策略（Guardrail）

```csharp
// IAiGuardrailPolicy
public interface IAiGuardrailPolicy
{
    /// <summary>检查 AI 发起的操作是否允许，返回拦截原因或通过</summary>
    Task<AiGuardrailResult> EvaluateAsync(
        long userId,
        AiToolCall toolCall,
        CancellationToken ct = default);
}

public sealed record AiGuardrailResult
{
    public required bool Allowed { get; init; }
    public string? DenyReason { get; init; }
    public bool RequiresApproval { get; init; }  // 高风险需人工确认
    public string? ApprovalPrompt { get; init; }
}
```

**默认风控规则（一期）**：

| 规则 | 阈值 | 动作 |
|------|------|------|
| 单笔市价单金额 | > 1,000 USDT | 需人工审批 |
| 单笔限价单金额 | > 5,000 USDT | 需人工审批 |
| 价格偏离 | 限价偏离最新价 > 5% | 拦截，提示 |
| 下单频率 | 1 分钟内 > 5 笔 | 拦截，冷却 |
| 自成交检测 | 买卖方向与现有反向挂单重叠 | 拦截 |
| 每日累计交易额 | > 10,000 USDT | 需人工审批 |

### 5.5 审批流程

```
高风险操作
    ↓
创建 ApprovalRecord（状态：Pending）
    ↓
前端弹窗 / 聊天卡片显示"待确认"
    ↓
用户点"确认" → /api/ai/approvals/{id}/approve
    ↓
重新执行操作 → 审计
```

### 5.6 审计日志

```csharp
public sealed record AiAuditEntry
{
    public required string Id { get; init; }
    public long UserId { get; init; }
    public string? ConversationId { get; init; }
    public required string Action { get; init; }        // ai.chat / ai.tool.call / ai.approval.approved
    public required string ModelId { get; init; }
    public required string ToolName { get; init; }      // 无工具则为 "none"
    public required string ToolArgumentsJson { get; init; }
    public required string ResultJson { get; init; }
    public required string Status { get; init; }        // success / denied / approved / failed
    public long ElapsedMs { get; init; }
    public DateTime CreatedAt { get; init; }
}
```

---

## 6. 二期：风控监控系统（拆分：确定性检测引擎 + AI 洞察层）

> **重要拆分**：异常检测本质上是**统计 + 规则**问题，用确定性代码实现更快、更可靠、可解释。AI 只在最后做**风险报告生成与解释**，不承担检测判断。

### 6-A 项目自身优化：确定性风控检测引擎（不需要 LLM）

激活 `RiskManagement` 角色，用**确定性规则 + 统计方法**做实时异常检测。

### 6-A.1 监控维度

| 维度 | 检测逻辑 | 输出 |
|------|----------|------|
| 异常大单 | 单笔成交量 > 该交易对 5 分钟均量的 N 倍 | 风控告警 |
| 价格闪崩 | 1 分钟内跌幅 > 阈值 | 熔断建议 + 暂停撮合 |
| 自成交/洗盘 | 检测同用户/关联账户对倒 | 标记 + 告警 |
| 频繁撤单 | 用户短时间大量挂单+撤单 | 限制或告警 |
| 仓位集中度 | 单一账户持仓占比过高 | 风险提示 |
| 异常登录/操作 | 登录地/操作频率异常 | 账户风险 |

### 6-A.2 实现方式（确定性引擎）

- 后台服务 `RiskMonitorService : BackgroundService`
- 周期性（如每 30s）聚合数据 → **规则/统计引擎判断**（无需 LLM）
- 告警通过 SignalR 推送到 `TradingHub` 的新方法 `RiskAlert`
- 高风险事件写入 `RiskEvent` 表，可人工处置
- 示例检测实现：`Z-Score 异常大单检测`、`窗口价格变动熔断`、`同用户对倒检测（确定性）`

### 6-B AI 洞察层（可选辅助，LLM 仅做解释，不做判断）

- 检测结果 → LLM 生成人类可读的风险报告（发生了什么、影响面、建议处置）
- **不参与检测判断本身**；LLM 不可用时只推送结构化告警（无报告），系统照常工作

### 6-A.3 二期新增接口

```
GET  /api/risk/events           — 风险事件列表
GET  /api/risk/dashboard        — 风控仪表盘数据
POST /api/risk/events/{id}/handle — 人工处置
```

---

## 7. 三期：项目自身优化——动态做市策略（确定性算法，不引入 AI）

> **重要结论（用户确认）**：做市商策略优化是**项目自身的量化优化**，不是 AI 功能。做市需要毫秒级响应、精确数值、可解释决策，LLM 的高延迟与输出不确定性完全不适合。用确定性量化算法实现即可，且效果更好。

### 7.1 现状问题

当前做市商（`AutoTradingLogicService.CreateMarketMakingOrdersAsync`）：
```csharp
var buyPrice = currentPrice.Price * 0.9995m;   // 固定 0.05% 价差
var sellPrice = currentPrice.Price * 1.0005m;
var quantity = (decimal)(Random.Shared.NextDouble() * 0.1 + 0.01); // 随机数量
```

**缺陷**：价差固定、数量随机、无视波动率和库存风险、无风险敞口管理。

### 7.2 确定性量化优化方案（不需要 AI）

| 策略输入 | 算法 | 影响 |
|----------|------|------|
| 波动率 | EWMA 估计近期收益波动率 σ | σ 大 → 价差加宽（价差 ∝ k·σ·P） |
| 订单簿不平衡度 | (买深 − 卖深) / (买深 + 卖深) | 单边失衡 → 调整偏置方向 |
| 库存偏离目标 | (当前库存 − 目标库存) / 目标库存 | 超买 → 压低买价、抬高卖价（去库存） |
| 最近成交频率 | 滑动窗口成交笔数 | 成交快 → 收窄价差，反之加宽 |

```
价差 = 基准价差 × (1 + λ·σ归一化)          // 波动率调整
偏置 = β·库存偏离 + γ·订单簿不平衡          // 方向调整
买价 = P × (1 − 价差/2 + 偏置)
卖价 = P × (1 + 价差/2 + 偏置)
```

**全部为纯数学计算，毫秒级响应，无 LLM 参与。**

### 7.3 实现

```csharp
public interface IMarketMakingStrategy
{
    MarketMakingDecision Decide(
        string symbol,
        MarketMakingContext context);
}

public sealed record MarketMakingDecision
{
    public required decimal BuyPrice { get; init; }
    public required decimal SellPrice { get; init; }
    public required decimal Quantity { get; init; }
    public required string Rationale { get; init; }  // 决策理由（审计用）
}
```

- `AutoTradingLogicService` 改造：从"固定规则"改为"调用 `IMarketMakingStrategy`"
- 实现类：`VolatilityAwareMarketMakingStrategy`（EWMA 波动率 + 库存 + 深度，纯数学，无 LLM）
- 可配置参数：基准价差、λ / β / γ、库存目标、最大敞口
- **保留降级**：策略抛异常/参数非法时回退到现有固定规则
- 每轮决策写入审计，可追踪策略质量

---

## 8. 四期：交易策略回测与信号（拆分：确定性引擎 + AI 策略描述解析）

> **重要拆分**：
> - **8-A 回测/信号引擎 = 项目自身优化**（确定性工程：历史数据模拟撮合、指标计算、收益/回撤/胜率统计，不需要 LLM）
> - **8-B AI 策略描述解析 = 真正需要 AI 的部分**（把用户自然语言描述映射为结构化回测参数；LLM 有幻觉风险，必须用枚举/约束 + 校验兜底）

### 8-A 项目自身优化：回测引擎 + 信号引擎（确定性，无 LLM）

### 8-A.1 功能

- 基于 KLineData 历史数据模拟成交：收益 / 最大回撤 / 胜率 / 夏普比率
- 实时信号：确定性技术指标组合（MA 交叉、RSI、布林带、MACD）→ 推送信号
- 所有计算用确定性代码，毫秒级响应，可单测

### 8-A.2 组件

```
回测引擎：历史 K 线 → 指标计算 → 模拟撮合 → 绩效统计（纯确定性）
信号引擎：实时 K 线 → 指标计算 → 规则判断 → 信号推送（纯确定性）
```

### 8-A.3 新增接口

```
POST /api/backtest           — 提交回测请求
GET  /api/backtest/{id}      — 回测结果
GET  /api/signals/{symbol}   — 实时信号
```

### 8-B AI 集成：自然语言策略描述 → 结构化回测配置（可选，二阶段再做）

```
用户："5日线上穿20日线时买入，跌破时卖出，每笔投入100 USDT"
  ↓ LLM 解析（枚举受支持的策略类型 + 参数抽取）
  ↓ 校验（策略类型在枚举内、参数在合法范围）
  ↓ 生成结构化 BacktestConfig → 交给 8-A 确定性引擎执行
```

- LLM 只做**参数抽取**，不做策略计算
- 解析失败 / 参数越界 → 拒绝并提示用户重新描述，不静默猜测
- 需要本地模型支持 tool calling / JSON 输出

---

## 9. 五期（N期）：AI 平台助手与更多

### 9.1 AI 平台客服助手

- 解答交易规则、费率、操作指引
- RAG：基于项目文档 + 平台规则知识库

### 9.2 AI 财报/新闻情感分析

- 接入新闻源 → LLM 情感分析 → 影响交易对走势提示

### 9.3 AI 个性化投资报告

- 定期生成用户持仓报告、盈亏分析、风险建议

### 9.4 AI 驱动的自动化测试

- 用 LLM 生成撮合引擎的边界测试用例

---

## 10. 数据模型

### 10.1 新增表（MySQL）

```sql
-- AI 会话
CREATE TABLE AiConversations (
    Id            BIGINT PRIMARY KEY AUTO_INCREMENT,
    UserId        BIGINT NOT NULL,
    Title         VARCHAR(200) NULL,
    CreatedAt     DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt     DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
);

-- AI 消息
CREATE TABLE AiMessages (
    Id             BIGINT PRIMARY KEY AUTO_INCREMENT,
    ConversationId BIGINT NOT NULL,
    Role           VARCHAR(20) NOT NULL,      -- system/user/assistant/tool
    Content        TEXT NULL,
    ToolCallId     VARCHAR(100) NULL,
    ToolCallsJson  TEXT NULL,
    CreatedAt      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_conversation (ConversationId)
);

-- AI 审计日志
CREATE TABLE AiAuditLogs (
    Id                 BIGINT PRIMARY KEY AUTO_INCREMENT,
    UserId             BIGINT NOT NULL,
    ConversationId     BIGINT NULL,
    Action             VARCHAR(50) NOT NULL,
    ModelId            VARCHAR(100) NULL,
    ToolName           VARCHAR(100) NOT NULL DEFAULT 'none',
    ToolArgumentsJson  TEXT NULL,
    ResultJson         TEXT NULL,
    Status             VARCHAR(20) NOT NULL,
    ElapsedMs          INT NOT NULL DEFAULT 0,
    CreatedAt          DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_user_time (UserId, CreatedAt)
);

-- AI 审批记录
CREATE TABLE AiApprovals (
    Id            BIGINT PRIMARY KEY AUTO_INCREMENT,
    UserId        BIGINT NOT NULL,
    AuditLogId    BIGINT NULL,
    ActionType    VARCHAR(100) NOT NULL,       -- place_order 等
    PayloadJson   TEXT NOT NULL,               -- 待执行的操作
    Status        VARCHAR(20) NOT NULL,        -- Pending/Approved/Rejected/Expired
    ExpiresAt     DATETIME NULL,
    CreatedAt     DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    DecidedAt     DATETIME NULL
);

-- 风险事件（二期）
CREATE TABLE RiskEvents (
    Id            BIGINT PRIMARY KEY AUTO_INCREMENT,
    EventType     VARCHAR(50) NOT NULL,
    Severity      VARCHAR(20) NOT NULL,        -- Low/Medium/High/Critical
    Symbol        VARCHAR(50) NULL,
    UserId        BIGINT NULL,
    DetailJson    TEXT NOT NULL,
    Status        VARCHAR(20) NOT NULL,        -- Open/Handled/Ignored
    CreatedAt     DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    HandledAt     DATETIME NULL,
    INDEX idx_status_time (Status, CreatedAt)
);
```

> 若一期先做轻量版，`AiConversations` / `AiMessages` / `AiAuditLogs` 可先用内存或 SQLite 落地，但建议直接用 MySQL（项目已有）。

### 10.2 新增强类型配置类

```csharp
// CryptoSpot.Infrastructure/Ai/AiOptions.cs
public sealed class AiOptions
{
    public string Provider { get; set; } = "OpenAiCompat";
    public string BaseUrl { get; set; } = "http://localhost:11434/v1";
    public string ApiKey { get; set; } = "ollama";
    public string Model { get; set; } = "qwen2.5-coder:32b";
    public string? ModelFast { get; set; }
    public double Temperature { get; set; } = 0.2;
    public int MaxTokens { get; set; } = 2048;
    public int TimeoutSeconds { get; set; } = 120;
    public bool EnableStreaming { get; set; } = true;
    public AiModelConfig[]? FallbackModels { get; set; }
}

public sealed class AiGuardrailOptions
{
    public decimal MarketOrderApprovalThreshold { get; set; } = 1000m;
    public decimal LimitOrderApprovalThreshold { get; set; } = 5000m;
    public decimal MaxPriceDeviationPercent { get; set; } = 5m;
    public int MaxOrdersPerMinute { get; set; } = 5;
    public decimal DailyTradeApprovalThreshold { get; set; } = 10000m;
}
```

---

## 11. API 设计

### 11.1 一期接口

```
POST /api/ai/chat                     — 通用 AI 对话（流式 SSE）
  Body: { "message": "...", "conversationId": "?" }
  Auth: Bearer Token
  返回: SSE 流（text/event-stream）

POST /api/ai/analyze                  — 行情分析（非流式，便于测试）
  Body: { "question": "BTC 什么情况？" }
  返回: { analysis, toolCalls, citations }

POST /api/ai/trade                    — 自然语言交易（Agentic）
  Body: { "instruction": "买入 0.01 BTC" }
  返回: { plan, approvals[], executedActions[], summary }

POST /api/ai/approvals/{id}/approve   — 审批通过
POST /api/ai/approvals/{id}/reject    — 审批拒绝
GET  /api/ai/conversations            — 会话列表
GET  /api/ai/conversations/{id}       — 会话消息
GET  /api/ai/audit                    — 审计日志（当前用户）
```

### 11.2 流式协议（SSE）

```
event: message
data: {"contentDelta":"BTC 现价"}

event: tool_call
data: {"tool":"get_klines","arguments":"{\"symbol\":\"BTCUSDT\"}"}

event: done
data: {"finishReason":"stop","auditId":"..."}
```

### 11.3 SignalR 扩展（二期）

```
TradingHub 新增方法：
  RiskAlert(riskEvent)      — 风控告警推送
  AiMessage(message)        — AI 消息推送（可选，替代 SSE）
```

---

## 12. 前端设计

### 12.1 AI 助手面板（新增组件）

```
frontend/src/
├── components/ai/
│   ├── AiAssistantPanel.tsx      — 主面板（聊天 UI）
│   ├── AiMessageBubble.tsx       — 消息气泡（支持 markdown）
│   ├── AiApprovalCard.tsx        — 审批确认卡片
│   ├── AiToolCallIndicator.tsx   — 工具调用状态指示
│   └── AiTypingIndicator.tsx     — 打字中动画
├── hooks/
│   └── useAiChat.ts              — AI 对话 hook（SSE 流式）
├── api/
│   └── ai.ts                     — AI API 封装
└── types/
    └── ai.ts                     — AI 类型定义
```

### 12.2 UI 布局

- 交易页面右侧/底部新增可折叠"AI 助手"面板
- 聊天消息支持 Markdown 渲染（分析报告）
- 审批卡片内嵌在聊天流中：`⚠️ 待确认：市价买入 0.5 BTC（约 33,000 USDT）[确认] [取消]`
- 工具调用显示为小徽标：`🔧 正在查询 BTCUSDT K线...`

### 12.3 交互细节

- 流式输出：SSE 逐字渲染分析结果
- 工具调用中：显示加载状态 + 工具名
- 审批：卡片式确认，超时自动失效（默认 5 分钟）

---

## 13. 配置设计

### 13.1 完整配置示例

```json
{
  "Ai": {
    "Provider": "OpenAiCompat",
    "BaseUrl": "http://localhost:11434/v1",
    "ApiKey": "ollama",
    "Model": "qwen2.5-coder:32b",
    "ModelFast": "qwen2.5-coder:7b",
    "Temperature": 0.2,
    "MaxTokens": 2048,
    "TimeoutSeconds": 120,
    "EnableStreaming": true,
    "FallbackModels": [
      {
        "BaseUrl": "http://localhost:11434/v1",
        "ApiKey": "ollama",
        "Model": "qwen2.5-coder:7b"
      },
      {
        "BaseUrl": "https://api.deepseek.com",
        "ApiKey": "sk-xxx",
        "Model": "deepseek-chat"
      }
    ]
  },
  "AiGuardrail": {
    "MarketOrderApprovalThreshold": 1000,
    "LimitOrderApprovalThreshold": 5000,
    "MaxPriceDeviationPercent": 5,
    "MaxOrdersPerMinute": 5,
    "DailyTradeApprovalThreshold": 10000
  }
}
```

### 13.2 环境变量覆盖

```
AI__BASEURL=http://localhost:11434/v1
AI__MODEL=qwen2.5-coder:32b
AI__APIKEY=ollama
```

---

## 14. 测试计划

### 14.1 单元测试

| 测试目标 | 内容 |
|----------|------|
| `OpenAiCompatAiModel` | Mock HttpClient，验证请求体、SSE 解析、工具参数累积 |
| `AiAgentOrchestrator` | Mock LLM，验证 ReAct 循环、最大轮次、工具调用 |
| `AiGuardrailPolicy` | 验证各规则阈值、审批触发、拦截原因 |
| 工具实现 | 每个 AiTool 的正确参数解析与底层服务调用 |
| 审计日志 | 每个动作是否正确落日志 |

### 14.2 集成测试

| 场景 | 验证 |
|------|------|
| 行情分析 | 真实/模拟 LLM + 真实数据服务，验证分析结果含真实价格 |
| 自然语言下单（低风险） | 自动执行，无需审批 |
| 自然语言下单（高风险） | 触发审批，审批后执行 |
| 审批拒绝 | 操作不执行，审计记录 rejected |
| LLM 超时降级 | 主模型失败 → 切换到备选模型 |

### 14.3 本地 LLM 冒烟测试

```bash
# 启动 Ollama
ollama serve
ollama pull qwen2.5-coder:32b

# 验证工具调用能力
curl http://localhost:11434/v1/chat/completions \
  -d '{"model":"qwen2.5-coder:32b","messages":[{"role":"user","content":"查一下BTC价格"}],"tools":[...]}'
```

---

## 15. 风险与边界

### 15.1 AI 边界（红线）

1. **AI 绝不直接访问数据库或撮合引擎**，必须通过现有 Application 层服务
2. **写操作必须有风控**，大额/异常操作必须人工审批
3. **AI 输出不构成投资建议的免责声明**，前端必须展示风险提示
4. **全链路审计**，任何 AI 决策可回溯

### 15.2 技术风险

| 风险 | 缓解 |
|------|------|
| 本地模型不支持 tool calling | 选型验证；或一期仅做纯分析，交易助手用支持 tool calling 的模型 |
| 本地模型推理慢 | 用 7b 快速模型做路由；设置超时 + 降级 |
| LLM 幻觉（编造价格） | 系统提示词强制"只基于工具数据"；工具结果注入验证 |
| 流式工具参数分片丢失 | 按 index 累积，流结束再解析（历史经验教训） |
| AI 误下单 | 风控 + 审批 + 每日限额 + 审计 |

### 15.3 合规边界

- 明确提示：AI 分析仅供参考，不构成投资建议
- 审批记录、审计日志需满足可追溯要求
- 若上线真实资金，需额外接入合规风控（KYC/AML），本设计仅限学习演示

---

## 附录 A：分阶段实施清单

### 一期（分析师 + 交易助手）
- [ ] `IAiModel` 抽象 + `OpenAiCompatAiModel` 实现
- [ ] `AiOptions` / `AiGuardrailOptions` 配置
- [ ] `AiTool` 只读工具集（6 个）
- [ ] `AiAgentOrchestrator`（ReAct 循环）
- [ ] `IAiGuardrailPolicy` + 默认实现
- [ ] `IAiApprovalService` + 审批流程
- [ ] `IAiAuditLog` + MySQL 落地
- [ ] `AiController`（chat/analyze/trade/approvals）
- [ ] 前端 AI 助手面板
- [ ] 单元测试 + 集成测试

### 二期（风控监控 —— 拆分）
- [ ] `RiskMonitorService` 后台服务（**确定性引擎**，无 LLM）
- [ ] `RiskEvent` 实体 + 仓储
- [ ] SignalR `RiskAlert` 推送
- [ ] 风控仪表盘前端
- [ ] （可选）AI 风险报告生成层（LLM 仅解释，不做判断）

### 三期（做市优化 —— 项目自身优化，非 AI）
- [ ] `IMarketMakingStrategy` 抽象（确定性实现）
- [ ] `VolatilityAwareMarketMakingStrategy`（EWMA 波动率 + 库存 + 深度）
- [ ] 改造 `AutoTradingLogicService`（保留固定规则降级）
- [ ] 做市决策审计

### 四期（回测与信号 —— 拆分）
- [ ] 回测引擎（确定性，项目自身优化）
- [ ] 信号引擎（确定性，项目自身优化）
- [ ] （可选）AI 策略描述解析器（LLM 只做参数抽取）

### 五期（N期，真正的 AI 功能）
- [ ] 平台客服（RAG）
- [ ] 新闻情感分析
- [ ] 个性化投资报告

---

> 本文档为等待实现的完整设计。实施时请遵循第 1.3 节设计原则，尤其注意：
> **AI 只做理解与编排，业务写操作走确定性服务，高风险必须审批，全链路审计。**
