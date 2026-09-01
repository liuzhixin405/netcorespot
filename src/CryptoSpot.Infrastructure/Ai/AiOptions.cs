namespace CryptoSpot.Infrastructure.Ai;

public sealed class AiOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434/v1";
    public string ApiKey { get; set; } = "ollama";
    public string Model { get; set; } = "qwen2.5:7b";
    public double Temperature { get; set; } = 0.2;
    public int MaxTokens { get; set; } = 2048;
    public int TimeoutSeconds { get; set; } = 120;
    public bool Enabled { get; set; } = true;
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
