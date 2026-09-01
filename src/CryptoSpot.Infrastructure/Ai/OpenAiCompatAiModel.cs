using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CryptoSpot.Application.Abstractions.Services.Ai;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace CryptoSpot.Infrastructure.Ai;

public sealed class OpenAiCompatAiModel : IAiModel
{
    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly ConcurrentDictionary<string, CircuitState> _circuits = new();

    public OpenAiCompatAiModel(HttpClient httpClient, IOptions<AiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public string ModelId => _options.Model;

    public async Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken cancellationToken = default)
    {
        foreach (var endpoint in Endpoints())
        {
            if (IsOpen(endpoint))
                continue;
            try
            {
                var result = await SendChatAsync(endpoint, request, cancellationToken);
                RecordSuccess(endpoint);
                return result;
            }
            catch (HttpRequestException)
            {
                RecordFailure(endpoint);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                RecordFailure(endpoint);
            }
        }
        throw new HttpRequestException("All configured AI model endpoints are unavailable.");
    }

    private async Task<AiChatResponse> SendChatAsync(AiModelEndpoint endpoint, AiChatRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{endpoint.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = endpoint.Model,
                messages = request.Messages.Select(message => new
                {
                    role = message.Role, content = message.Content, tool_call_id = message.ToolCallId,
                    tool_calls = message.ToolCalls?.Select(call => new { id = call.Id, type = "function", function = new { name = call.Name, arguments = call.ArgumentsJson } })
                }),
                tools = request.Tools?.Select(tool => new { type = "function", function = new { name = tool.Name, description = tool.Description, parameters = JsonSerializer.Deserialize<JsonElement>(tool.ParametersJsonSchema) } }),
                temperature = request.Temperature,
                max_tokens = request.MaxTokens,
                stream = false
            })
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.ApiKey);
        using var response = await _httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        var choices = document.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
            throw new InvalidOperationException("The AI provider returned no choices.");

        var responseMessage = choices[0].GetProperty("message");
        var content = responseMessage.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String
            ? contentElement.GetString()
            : null;
        var toolCalls = responseMessage.TryGetProperty("tool_calls", out var callsElement)
            ? callsElement.EnumerateArray().Select(call => new AiToolCall(
                call.GetProperty("id").GetString() ?? throw new InvalidOperationException("AI tool call ID is missing."),
                call.GetProperty("function").GetProperty("name").GetString() ?? throw new InvalidOperationException("AI tool name is missing."),
                call.GetProperty("function").GetProperty("arguments").GetString() ?? "{}")).ToArray()
            : Array.Empty<AiToolCall>();
        if (string.IsNullOrWhiteSpace(content) && toolCalls.Length == 0)
            throw new InvalidOperationException("The AI provider returned an empty response.");

        var finishReason = choices[0].TryGetProperty("finish_reason", out var finishElement)
            ? finishElement.GetString()
            : null;
        return new AiChatResponse(content ?? string.Empty, toolCalls, finishReason);
    }

    private IEnumerable<AiModelEndpoint> Endpoints()
    {
        yield return new AiModelEndpoint { BaseUrl = _options.BaseUrl, ApiKey = _options.ApiKey, Model = _options.Model };
        foreach (var endpoint in _options.FallbackModels.Where(value =>
                     !string.IsNullOrWhiteSpace(value.BaseUrl) && !string.IsNullOrWhiteSpace(value.Model)))
            yield return endpoint;
    }

    private bool IsOpen(AiModelEndpoint endpoint) =>
        _circuits.TryGetValue(endpoint.BaseUrl, out var state) &&
        state.OpenUntil > DateTimeOffset.UtcNow;

    private void RecordSuccess(AiModelEndpoint endpoint) => _circuits.TryRemove(endpoint.BaseUrl, out _);

    private void RecordFailure(AiModelEndpoint endpoint)
    {
        _circuits.AddOrUpdate(endpoint.BaseUrl,
            _ => new CircuitState(1, DateTimeOffset.MinValue),
            (_, state) =>
            {
                var failures = state.Failures + 1;
                return failures >= _options.CircuitBreakerFailureThreshold
                    ? new CircuitState(failures, DateTimeOffset.UtcNow.AddSeconds(_options.CircuitBreakerCooldownSeconds))
                    : new CircuitState(failures, state.OpenUntil);
            });
    }

    private sealed record CircuitState(int Failures, DateTimeOffset OpenUntil);

    public async IAsyncEnumerable<AiChatChunk> ChatStreamAsync(
        AiChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = _options.Model,
                messages = request.Messages.Select(value => new { role = value.Role, content = value.Content }),
                temperature = request.Temperature,
                max_tokens = request.MaxTokens,
                stream = true
            })
        };
        using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var toolCalls = new Dictionary<int, StreamingToolCall>();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;
            var data = line[6..];
            if (data == "[DONE]")
                yield break;

            using var document = JsonDocument.Parse(data);
            var choice = document.RootElement.GetProperty("choices")[0];
            var delta = choice.TryGetProperty("delta", out var deltaElement) &&
                deltaElement.TryGetProperty("content", out var contentElement)
                ? contentElement.GetString()
                : null;
            var finishReason = choice.TryGetProperty("finish_reason", out var finishElement)
                ? finishElement.GetString()
                : null;
            if (choice.TryGetProperty("delta", out deltaElement) &&
                deltaElement.TryGetProperty("tool_calls", out var toolCallsElement))
            {
                foreach (var toolCall in toolCallsElement.EnumerateArray())
                {
                    var index = toolCall.GetProperty("index").GetInt32();
                    if (!toolCalls.TryGetValue(index, out var accumulated))
                    {
                        accumulated = new StreamingToolCall();
                        toolCalls[index] = accumulated;
                    }
                    if (toolCall.TryGetProperty("id", out var id))
                        accumulated.Id = id.GetString() ?? accumulated.Id;
                    if (toolCall.TryGetProperty("function", out var function))
                    {
                        if (function.TryGetProperty("name", out var name))
                            accumulated.Name = name.GetString() ?? accumulated.Name;
                        if (function.TryGetProperty("arguments", out var arguments))
                            accumulated.Arguments.Append(arguments.GetString());
                    }
                }
            }
            var completedCalls = finishReason == "tool_calls"
                ? toolCalls.OrderBy(value => value.Key).Select(value => new AiToolCall(
                    value.Value.Id ?? throw new InvalidOperationException("Streamed tool call ID is missing."),
                    value.Value.Name ?? throw new InvalidOperationException("Streamed tool name is missing."),
                    value.Value.Arguments.ToString())).ToArray()
                : null;
            if (delta is not null || finishReason is not null)
                yield return new AiChatChunk(delta, completedCalls, finishReason);
        }
    }

    private sealed class StreamingToolCall
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public StringBuilder Arguments { get; } = new();
    }
}
