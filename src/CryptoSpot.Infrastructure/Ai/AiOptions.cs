namespace CryptoSpot.Infrastructure.Ai;

public sealed class AiOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:9931/v1";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "Qwen3-4B";
    public double Temperature { get; set; } = 0.2;
    public int MaxTokens { get; set; } = 768;
    public int TimeoutSeconds { get; set; } = 420;
    public bool Enabled { get; set; } = true;
    /// <summary>向 llama.cpp/Qwen3 发送 enable_thinking=false，避免本地小模型把 token 全耗在思考上。</summary>
    public bool DisableThinking { get; set; } = true;
    public AiModelEndpoint[] FallbackModels { get; set; } = Array.Empty<AiModelEndpoint>();
    public int CircuitBreakerFailureThreshold { get; set; } = 3;
    public int CircuitBreakerCooldownSeconds { get; set; } = 60;
}

public sealed class AiModelEndpoint
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}
