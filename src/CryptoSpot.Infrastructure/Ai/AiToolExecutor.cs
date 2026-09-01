using System.Text.Json;
using CryptoSpot.Application.Abstractions.Services.Ai;
using CryptoSpot.Application.Abstractions.Services.Trading;

namespace CryptoSpot.Infrastructure.Ai;

public sealed class AiToolExecutor : IAiToolExecutor
{
    private const string SymbolSchema = """{"type":"object","properties":{"symbol":{"type":"string"}},"required":["symbol"],"additionalProperties":false}""";
    private readonly ITradingService _tradingService;

    public AiToolExecutor(ITradingService tradingService)
    {
        _tradingService = tradingService;
    }

    public IReadOnlyList<AiToolDefinition> Definitions { get; } = new[]
    {
        new AiToolDefinition("get_ticker", "获取交易对实时行情。", SymbolSchema),
        new AiToolDefinition("get_klines", "获取交易对最近 24 根一小时 K 线。", SymbolSchema),
        new AiToolDefinition("get_orderbook", "获取交易对前 10 档订单簿。", SymbolSchema),
        new AiToolDefinition("get_assets", "获取当前用户资产。", """{"type":"object","properties":{},"additionalProperties":false}"""),
        new AiToolDefinition("get_open_orders", "获取当前用户指定交易对的未完成订单。", SymbolSchema),
        new AiToolDefinition("get_recent_trades", "获取市场最近成交。", SymbolSchema)
    };

    public async Task<string> ExecuteAsync(long userId, AiToolCall toolCall, CancellationToken cancellationToken = default)
    {
        var symbol = GetSymbol(toolCall.ArgumentsJson);
        object data = toolCall.Name switch
        {
            "get_ticker" => await GetData(_tradingService.GetTradingPairAsync(symbol)),
            "get_klines" => await GetData(_tradingService.GetKLineDataAsync(symbol, "1h", 24)),
            "get_orderbook" => await GetData(_tradingService.GetOrderBookDepthAsync(symbol, 10)),
            "get_assets" => await GetData(_tradingService.GetUserAssetsAsync(userId)),
            "get_open_orders" => await GetData(_tradingService.GetOpenOrdersAsync(userId, symbol)),
            "get_recent_trades" => await GetData(_tradingService.GetMarketRecentTradesAsync(symbol, 50)),
            _ => throw new InvalidOperationException($"AI attempted to use unsupported tool '{toolCall.Name}'.")
        };
        return JsonSerializer.Serialize(data);
    }

    private static string GetSymbol(string argumentsJson)
    {
        using var document = JsonDocument.Parse(argumentsJson);
        var symbol = document.RootElement.TryGetProperty("symbol", out var value) ? value.GetString() : null;
        return string.IsNullOrWhiteSpace(symbol) ? "BTCUSDT" : symbol.Trim().ToUpperInvariant();
    }

    private static async Task<object> GetData<T>(Task<CryptoSpot.Application.DTOs.Common.ApiResponseDto<T>> task)
    {
        var response = await task;
        if (!response.Success)
            return new { error = response.Error ?? "Data query failed." };
        return response.Data is null ? new { error = "No data returned." } : response.Data;
    }
}
