# 深入 netcorespot：一个 .NET 9 全栈交易系统里，AI 是如何被「关进笼子」的

> 项目：CryptoSpot（仓库名 netcorespot）
> 地址：https://github.com/liuzhixin405/netcorespot
> 技术栈：.NET 9 / ASP.NET Core · React 18 · SignalR + MessagePack · MySQL(EF Core) · OpenAI 兼容协议

很多人做「AI + 交易」的 Demo，止步于让模型聊两句行情、给点建议。这个项目不一样的地方在于：**它让 AI 真正能下单，但把 AI 的每一个动作都关进了一个由「工具隔离 + 规则风控 + 人工审批 + 全链路审计 + 模型容错」组成的笼子里。**

本文不写「怎么跑起来」这种入门内容（README 里已经很全），而是聚焦两件事：**核心交易引擎是怎么设计的，以及 AI 这整条链路是怎么被工程化约束起来的。**

---

## 一、总体架构：五层，依赖只朝一个方向

后端是一个典型的 Clean Architecture 五层结构：

```
CryptoSpot.Domain            实体、枚举、仓储接口（无任何依赖）
        ▲
CryptoSpot.Application       DTO、服务抽象(IAiModel/ITradingService/...)、接口
        ▲
CryptoSpot.Infrastructure    实现层：撮合引擎、行情源、AI、SignalR、后台服务
        ▲
CryptoSpot.Persistence       EF Core DbContext、仓储实现
        ▲
CryptoSpot.API               控制器、中间件、Program.cs（组合根）
```

关键点：**所有「AI 能干什么」的能力，在 Application 层只以接口形式存在**。比如 AI 统一抽象 `IAiModel`、只读工具执行器 `IAiToolExecutor`、风控/审计/审批服务，都定义在 `Application.Abstractions` 里，`Infrastructure` 才给出具体实现。这个分层是后面整套 AI 安全设计能成立的前提——业务规则与「模型供应商」彻底解耦。

`Program.cs` 是纯组合根，用扩展方法 `AddInfrastructure(configuration)`、`AddCleanArchitecture()` 一次性装配。值得注意的几处基础设施：

- **JWT**：标准 Bearer 认证，同时支持 `access_token` 走 query string（SignalR WebSocket 握手时浏览器没法加 header）。
- **限流**：`AddRateLimiter` 用固定窗口，`auth` 接口 1 分钟 5 次。
- **JSON**：`long` 会被序列化成字符串（`LongToStringJsonConverter`），避免 JS 精度丢失——这是交易系统里很常见但容易被忽略的坑。
- **异常**：`UseGlobalExceptionHandling()` 全局中间件在最前。
- **健康检查**：自定义 `AddCryptoSpotHealthChecks` + 启动时 `PerformStartupHealthChecks`。

---

## 二、撮合引擎：进程内 Channel + 价格时间优先

撮合引擎在 `CryptoSpot.Infrastructure/MatchEngine`，核心是 `ChannelMatchEngineService`，纯内存、无 Redis 依赖（配置文件里的 Redis 段仅历史保留）。

### 2.1 每个交易对一个「订单簿 + 一个有界 Channel + 一个消费循环」

```csharp
private readonly ConcurrentDictionary<string, IOrderBook> _orderBooks = new();
private readonly ConcurrentDictionary<string, Channel<OrderRequest>> _orderChannels = new();
private readonly ConcurrentDictionary<string, Task> _processingTasks = new();

public void InitializeSymbol(string symbol)
{
    var normalizedSymbol = NormalizeSymbol(symbol);
    if (!_orderBooks.TryAdd(normalizedSymbol, new InMemoryOrderBook(normalizedSymbol)))
        return;

    var channel = Channel.CreateBounded<OrderRequest>(new BoundedChannelOptions(10000)
    {
        FullMode = BoundedChannelFullMode.Wait
    });
    _orderChannels[normalizedSymbol] = channel;

    _processingTasks[normalizedSymbol] = Task.Run(async () =>
    {
        await foreach (var request in channel.Reader.ReadAllAsync(_cts.Token))
        {
            try
            {
                if (request.Type == OrderRequestType.Cancel) { ProcessCancelOrder(...); continue; }
                await ProcessSingleOrderAsync(request.Order, orderBook, symbol, _cts.Token);
            }
            catch (Exception ex) { request.Completion?.TrySetException(ex); }
        }
    });
}
```

设计意图很清晰：

1. **串行化撮合**：每个交易对只有一个消费者任务，天然避免了同一订单簿上的并发写，不需要在订单簿内部加锁（`InMemoryOrderBook` 的 `GetBestOpposite` 也就无需线程安全）。
2. **背压**：`BoundedChannelFullMode.Wait`，写入端满 10000 时阻塞等待，而不是无限膨胀内存。
3. **下单路径**：`PlaceOrderAsync` 先 `FreezeAssetAsync` 冻结资产（失败直接抛「余额不足」），再把订单入队，保证「先占款、后撮合」。

### 2.2 价格时间优先算法

`PriceTimePriorityMatchingAlgorithm.Match(book, taker)` 是一个 `yield return` 的迭代器，逐个产出 `MatchSlice`：

```csharp
public IEnumerable<MatchSlice> Match(IOrderBook book, Order taker)
{
    while (taker.FilledQuantity < taker.Quantity)
    {
        var maker = book.GetBestOpposite(taker.Side);
        if (maker == null) yield break;                 // 对手盘为空
        if (!PriceCross(taker, maker)) yield break;      // 价格不交叉

        if (maker.UserId == taker.UserId)                // 自成交防护
        { book.Remove(maker); continue; }

        var qty = Math.Min(taker.Quantity - taker.FilledQuantity,
                           maker.Quantity - maker.FilledQuantity);
        var price = maker.Price ?? taker.Price ?? 0m;    // 以 maker 价为成交价

        yield return new MatchSlice(maker, taker, price, qty);
    }
}
```

- **价格交叉判断**：买单要求 `taker.Price >= maker.Price`，卖单反之；市价单直接视为可成交。
- **自成交防护**：同用户的两笔单直接跳过（移除 maker 后继续）。
- **成交价取 maker 价**：符合主流撮合惯例，taker 吃到更优的价格。

### 2.3 结算与持久化的细节

`ProcessSingleOrderAsync` 里，每产出一个 `MatchSlice` 就 `SettleTradeAsync` 做**纯内存资产结算**（买方解冻计价币、增加基准币；卖方解冻基准币、增加计价币），再累加双方 `FilledQuantity`、更新状态、发布 SignalR 事件。

持久化有两点值得学习：

1. **taker 单独持久化 + 受影响 maker 批量持久化**，用 `affectedMakers` 集合去重后单次 DB 往返，避免一单吃多档时对每个 maker 各打一次 UPDATE。
2. **成交记录 `Id=0` 交给数据库自增**，内存里用 `Interlocked` 生成临时 id，避免并发下主键冲突（注释里明确写了 `// 数据库自增，避免 Duplicate entry`）。

撮合中「结算失败则 break 终止」、`CancelOrderAsync` 用 `TaskCompletionSource` + `WaitAsync` 等待消费循环处理结果，这些都属于生产级的健壮性处理。

---

## 三、行情源：OKX WebSocket + Binance REST 兜底

- **实时**：OKX 公共 WebSocket（`business` K 线）驱动内存订单簿与价格。
- **兜底**：`BinanceMarketDataProvider` 提供 REST 历史 K 线，带 `ProxyUrl` 代理配置，并通过 `AddStandardResilienceHandler()` 加了重试/超时/熔断的 Resilience 策略。
- **异步桥接**：后台服务把 WebSocket 数据推送到内存，`ITradingService` 暴露统一查询接口（ticker / klines / orderbook / recent trades），AI 的只读工具底层调的就是这些。

这种「实时通道 + REST 兜底」的双源设计，是行情类系统里很实用的稳定思路。

---

## 四、AI 增强：重头戏

这是全文的重点。整个 AI 体系可以拆成六块来看。

### 4.1 统一抽象 `IAiModel`，彻底解耦供应商

Application 层只有一个模型抽象，Infrastructure 用 `OpenAiCompatAiModel` 实现，走 OpenAI 兼容的 `/chat/completions`：

```csharp
public interface IAiModel
{
    string ModelId { get; }
    Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken ct = default);
    IAsyncEnumerable<AiChatChunk> ChatStreamAsync(AiChatRequest request, CancellationToken ct = default);
}
```

默认配置指向本地 Ollama（`http://localhost:11434/v1` + `qwen2.5:7b`），但只要改 `appsettings` 的 `Ai` 节点，就能换成 DeepSeek / GLM / Qwen / llama.cpp 等任何 `/v1` 兼容端点，**代码零改动**。

### 4.2 非流式与流式（SSE）两种调用

**非流式 `ChatAsync`** 的请求体把 `messages`、`tools`、`temperature`、`max_tokens` 全部透传，并把工具定义里的 JSON Schema 字符串 `Deserialize<JsonElement>` 后回填，保证发给模型的 schema 是合法 JSON。响应解析时同时取 `content` 和 `tool_calls`，并且校验「content 和 tool_calls 不能同时为空」，空响应直接抛异常——避免静默吞掉异常响应。

**流式 `ChatStreamAsync`** 走 SSE（`text/event-stream`），逐行解析 `data: ` 前缀，识别 `[DONE]`。这里有一个很关键的细节——**流式工具调用参数的累积**：

```csharp
// 工具调用参数是分片到达的：{"  ... {"x":100 ... }，必须按 index 累积
var toolCalls = new Dictionary<int, StreamingToolCall>();
...
if (delta.TryGetProperty("tool_calls", out var toolCallsElement))
{
    foreach (var toolCall in toolCallsElement.EnumerateArray())
    {
        var index = toolCall.GetProperty("index").GetInt32();
        if (!toolCalls.TryGetValue(index, out var accumulated))
        { accumulated = new StreamingToolCall(); toolCalls[index] = accumulated; }
        if (toolCall.TryGetProperty("id", out var id)) accumulated.Id = id.GetString();
        if (toolCall.TryGetProperty("function", out var function))
        {
            if (function.TryGetProperty("name", out var name)) accumulated.Name = name.GetString();
            if (function.TryGetProperty("arguments", out var arguments)) accumulated.Arguments.Append(arguments.GetString());
        }
    }
}
// 只有当 finish_reason == "tool_calls" 时才把累积结果拼成完整 AiToolCall
```

这是很多 OpenAI 兼容流式实现最容易踩的坑：模型流式返回工具参数是**逐 token 分片**的，如果拿到一块就 `JsonDocument.Parse`，一定会解析失败。这里选择了「按 index 累积到 StringBuilder，流结束时统一组装」的正确做法。

### 4.3 工具系统：读写分离 + 严格参数校验

工具分两类，边界非常清楚：

**只读工具（`AiToolExecutor`，6 个）**——用于分析，绝不能改状态：

| 工具 | 说明 |
| --- | --- |
| `get_ticker` | 实时行情 |
| `get_klines` | 最近 24 根 1h K 线 |
| `get_orderbook` | 前 10 档订单簿 |
| `get_assets` | 当前用户资产 |
| `get_open_orders` | 未完成订单 |
| `get_recent_trades` | 市场最近成交（50 条） |

**写工具（`AiTradeService` 内，3 个）**——用于交易，全部经过风控：

| 工具 | 说明 |
| --- | --- |
| `place_order` | 下市价/限价单 |
| `cancel_order` | 撤单 |
| `cancel_all_orders` | 批量撤单（必审批） |

每个工具都有严格的 JSON Schema（`additionalProperties:false`，`quantity` 用 `exclusiveMinimum:0`）。更关键的是**入参二次校验**：`ParseOrder` 里 `EnsureOnlyProperties` 只允许白名单字段，`GetRequiredString` / `GetRequiredDecimal` 强校验 side/type/quantity/price，任何不合法的参数直接抛 `ArgumentException` 返回 `denied`——**即使用户能注入恶意 tool call，也过不了参数层**。

### 4.4 分析链路 `AiAnalyzer`：多轮工具调用 + 数据引用

`AiAnalyzer.AnalyzeAsync` 是一条典型的多轮 ReAct 循环：

```csharp
for (var round = 0; round < 5; round++)
{
    response = await _model.ChatAsync(...);          // 带 Tools
    if (response.ToolCalls is not { Count: > 0 })
        return new AiAnalysisResult(response.Content, citations);

    messages.Add(new AiMessage("assistant", response.Content, ToolCalls: response.ToolCalls));
    foreach (var toolCall in response.ToolCalls)
    {
        var result = await _tools.ExecuteAsync(userId, toolCall, ct);
        citations.Add(toolCall.Name);
        await _audit.WriteAsync(... "ai.tool.call" ...);
        messages.Add(new AiMessage("tool", result, toolCall.Id));
    }
}
throw new InvalidOperationException("AI analysis exceeded the maximum of five tool-call rounds.");
```

三个设计点：

1. **最多 5 轮工具调用**，防止模型陷入无限调用工具的循环。
2. **`citations` 收集**：模型最终结论里引用了哪些数据工具，前端可展示「本结论基于：get_ticker / get_klines」，从机制上对抗幻觉。
3. **系统提示词强约束**：「只允许依据提供的真实平台数据作答，禁止编造价格、持仓或指标」，且每次工具结果都回填给模型。

### 4.5 交易链路 `AiTradeService`：规划 → 风控 → 审批 → 二次校验执行

这是整个项目最核心、也最能体现「AI 能干但不能乱来」的地方。

`PlanAndExecuteAsync` 的编排与分析链路类似（5 轮上限、`Temperature = 0`、`MaxTokens = 512`），但关键在 `ExecuteToolAsync` 对写工具的 `switch` 分发：

```csharp
return toolCall.Name switch
{
    "place_order"     => await PlanOrSubmitOrderAsync(...),
    "cancel_order"    => await CancelOrderAsync(...),
    "cancel_all_orders" => await CreateCancellationApprovalAsync(...),
    _ => throw new InvalidOperationException($"AI attempted to use unsupported tool '{toolCall.Name}'.")
};
```

`place_order` 会先走 `EvaluateOrderAsync` 做**下单前风控评估**，评估结果决定三种走向：

- **`DenyReason != null`** → 直接拒绝（`denied`），不落库、不审批。
- **`RequiresApproval == true`** → 生成 Pending 审批单，返回 `pending_approval` + `approvalId`。
- **否则** → `SubmitOrderAsync` 直接提交。

审批通过后的执行并不是「直接提交」，而是 `ExecuteApprovedAsync` **再次跑一遍 `EvaluateOrderAsync`**：

```csharp
case "place_order":
    var plan = ParseOrder(approval.PayloadJson);
    var evaluation = await EvaluateOrderAsync(userId, plan, ct);
    outcome = evaluation.DenyReason is not null
        ? await DenyAsync(...)
        : await SubmitOrderAsync(...);
```

这个「审批时风控一次、执行时再风控一次」的双重校验很关键——因为审批到真正执行之间有时间差，市场价可能已经变了，必须用最新价格重新算偏离度。

### 4.6 风控规则详解（`AiGuardrailOptions`）

默认阈值（全部可配）：

| 规则 | 默认值 | 动作 |
| --- | --- | --- |
| 市价单审批阈值 | 1000 USDT | 超过 → 转审批 |
| 限价单审批阈值 | 5000 USDT | 超过 → 转审批 |
| 价格偏离 | 5% | 超过 → 直接拒绝 |
| 下单频率 | 5 次/分钟 | 超过 → 直接拒绝 |
| 日累计成交额 | 10000 USDT | 超过 → 后续转审批 |

`EvaluateOrderAsync` 的完整检查链：

1. 订单有效性（数量、交易对、限价必须有价）。
2. 当前价可用（拿不到价格直接拒，**绝不盲目下单**）。
3. 限价偏离最新价 > 5% → 拒。
4. 最近 1 分钟内 `place_order` 成功次数 ≥ 5 → 拒（查审计日志表统计）。
5. **自成交防护**：与用户反向挂单价交叉 → 拒。
6. 日累计成交额（解析历史审计日志里的 `notional` 累加）+ 本单 > 阈值 → 转审批。

注意第 4、6 条：**风控数据来源是 AI 审计日志本身**，形成了一个自洽的「审计即数据」闭环，不需要额外建风控计数表。

### 4.7 审批与审计闭环

- **审批**：`AiApprovalService` 生成 Pending 审批单（`ActionType` + `PayloadJson`），用户通过 `POST /api/ai/approvals/{id}/approve|reject` 决策。approve 后由 `ExecuteApprovedAsync` 真正执行，状态流转 `Pending → Approved → Executed/Failed`。
- **审计**：`AiAuditService` 贯穿全链路，动作类型覆盖 `ai.analyze`、`ai.tool.call`、`ai.tool.execute`、`ai.trade.plan`、`ai.approval.approved/rejected`、`ai.docs.answer`、`ai.news.sentiment`、`ai.portfolio.report`，每条记录都含 `userId`、`modelId`、`toolName`、`arguments`、`result`、`status`、`elapsedMs`。出问题时可以精确还原「AI 在什么时间、用哪个模型、调了什么工具、给了什么参数、结果是什么」。

### 4.8 扩展能力：文档助手 / 新闻情绪 / 持仓报告

`AiExpansionService` 实现了三个「增强型」AI 能力，它们的共同设计是 **LLM 为主 + 确定性 fallback 兜底**：

**① 文档助手（一个轻量 RAG）**

- 启动时用 `Lazy` 加载 `docs/` 下所有 `.md`，按 `\n\n` 切块（单块 ≤ 1400 字符）。
- 检索：把问题 tokenize（正则取英文/数字/下划线 token + 中文字符拆成单字），`score = 交集数 + 包含原问题 ? 10 : 0`，取 Top 4。
- 命中后交给 LLM 基于片段作答；**LLM 调用抛 `HttpRequestException` / 超时，则回退到「抽取式答案」**——直接把最相关的片段原文返回给用户。

**② 新闻情绪分析**

- 先走**词法分析**（`AnalyzeLexically`）得到确定性基线：命中利好词（上涨/获批/突破/bullish…）加分、利空词（暴跌/黑客/处罚/bearish…）减分，算出 `[-1,1]` 的 score。
- LLM 可用时覆盖为模型结果，并做**严格 JSON 校验**（`sentiment` 必须三选一、`score` 必须可解析、`summary` 非空，否则抛异常走 fallback）。

**③ 持仓报告**

- 注入**真实数据**（资产列表、近 10 笔成交、总估值）作为上下文，让 LLM 生成中文报告；失败则回退到结构化模板。

这三个能力展示了同一种工程范式：**AI 是不可靠的，所以永远要有一条「没有 AI 也能给出合理结果」的兜底路径**。

### 4.9 模型容错：熔断 + 降级

`OpenAiCompatAiModel` 内置了简易熔断器：

```csharp
private readonly ConcurrentDictionary<string, CircuitState> _circuits = new();

private bool IsOpen(AiModelEndpoint endpoint) =>
    _circuits.TryGetValue(endpoint.BaseUrl, out var state) &&
    state.OpenUntil > DateTimeOffset.UtcNow;

private void RecordFailure(AiModelEndpoint endpoint)
{
    _circuits.AddOrUpdate(endpoint.BaseUrl, _ => new CircuitState(1, ...),
        (_, state) =>
        {
            var failures = state.Failures + 1;
            return failures >= options.CircuitBreakerFailureThreshold  // 默认 3 次
                ? new CircuitState(failures, DateTimeOffset.UtcNow.AddSeconds(options.CircuitBreakerCooldownSeconds)) // 默认 60s
                : new CircuitState(failures, state.OpenUntil);
        });
}
```

配合 `Endpoints()` 依次产出「主模型 + `FallbackModels` 备用列表」，`ChatAsync` / `ChatStreamAsync` 会遍历所有端点，跳过熔断中的，任一成功即返回。**主模型连续失败 3 次 → 熔断 60 秒 → 自动切换备用模型**，全链路对调用方透明。同时每次请求都套 `CreateTimeoutTokenSource` 做超时控制。

---

## 五、实时推送：SignalR + MessagePack

`Program.cs` 里 `AddSignalR().AddMessagePackProtocol()`，Hub 映射到 `/tradingHub`，推送价格、订单簿、K 线、订单、风控事件。MessagePack 相比 JSON 更省带宽，适合高频行情推送。AI 的 SSE 对话流走 `text/event-stream`（`event: message` / `event: done`），与 SignalR 分开，各司其职。

---

## 六、AI 相关 API 一览

| 端点 | 作用 |
| --- | --- |
| `POST /api/ai/analyze` | 行情分析（含 citations、会话、审计） |
| `POST /api/ai/trade` | 自然语言交易（规划 → 风控 → 审批） |
| `POST /api/ai/approvals/{id}/approve` / `reject` | 审批决策 |
| `POST /api/ai/chat` | SSE 流式对话 |
| `POST /api/ai/assistant` | 文档助手（RAG） |
| `POST /api/ai/news/sentiment` | 新闻情绪 |
| `POST /api/ai/portfolio/report` | 持仓报告 |
| `GET /api/ai/conversations` / `{id}` | 会话管理 |
| `GET /api/ai/audit` | 审计查询 |

所有 AI 端点都在 `[Authorize]` 下，且通过 `ICurrentUserService` 拿当前用户 id，AI 工具执行时也只能操作「当前用户」的资产与订单，天然做了租户隔离。

---

## 七、配置与体验

后端 `appsettings` 的 `Ai` 节点即可控制一切：

```jsonc
"Ai": {
  "Enabled": true,
  "BaseUrl": "http://localhost:11434/v1",
  "ApiKey": "ollama",
  "Model": "qwen2.5:7b",
  "Temperature": 0.2,
  "MaxTokens": 2048,
  "TimeoutSeconds": 120,
  "FallbackModels": [],
  "CircuitBreakerFailureThreshold": 3,
  "CircuitBreakerCooldownSeconds": 60
}
```

不想要 AI 就把 `Enabled` 设为 false，交易、撮合、行情、推送等其余功能完全不受影响——**AI 是插拔式的能力，而不是系统的命脉**，这本身就是架构解耦的证明。

---

## 八、总结：这套设计可以复制到任何「AI Agent 操作真实业务」的场景

netcorespot 最值得借鉴的不是某个 AI 接口写得漂亮，而是那一整套**工程约束**：

1. **抽象解耦**：`IAiModel` + OpenAI 兼容协议，供应商可替换、AI 可开关。
2. **工具读写分离**：读工具随意用，写工具全部过风控。
3. **参数强校验**：JSON Schema + 白名单字段 + 类型强校验，堵死注入。
4. **多轮上限 + 数据引用**：防工具循环，防幻觉。
5. **规则风控 + 人工审批 + 二次校验**：AI 出牌前先过规则，审批通过后执行前再过一遍。
6. **全链路审计**：审计日志反过来又成为风控的计数来源，自洽闭环。
7. **熔断 + 备用模型 + 超时**：模型不可靠，就用工程手段兜住。
8. **确定性 fallback**：文档助手、新闻情绪、持仓报告都有「没有 LLM 也能出结果」的兜底路径。

把「交易」换成「审批、报销、下单、发消息」等任何真实业务动作，这套模式都成立。这正是它比一个「会聊天的交易机器人」更有价值的根本原因。

---

> ⚠️ 免责声明：本项目为学习/演示用途，撮合与行情均为模拟/公共数据，不构成任何投资建议。
