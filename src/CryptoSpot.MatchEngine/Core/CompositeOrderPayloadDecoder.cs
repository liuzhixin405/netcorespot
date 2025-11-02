using System;
using System.Text.Json;
using CryptoSpot.Application.DTOs.Trading;
using MessagePack;
using MessagePack.Resolvers;

namespace CryptoSpot.MatchEngine.Core
{
    /// <summary>
    /// 组合解码器：尝试 JSON DTO / 包裹 JSON / Base64 MessagePack，与现有 Worker 逻辑保持一致。
    /// </summary>
    public class CompositeOrderPayloadDecoder : IOrderPayloadDecoder
    {
        public bool TryDecode(string payload, out CreateOrderRequestDto? dto, out int userId, out string? error)
        {
            dto = null; userId = 0; error = null;
            // 1. 直接 JSON
            try
            {
                dto = JsonSerializer.Deserialize<CreateOrderRequestDto>(payload);
                if (dto != null) return true;
            }
            catch { }

            // 2. 包裹 JSON
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                if (root.TryGetProperty("userId", out var u) && u.ValueKind == JsonValueKind.Number)
                    userId = u.GetInt32();
                JsonElement? orderEl = null;
                if (root.TryGetProperty("order", out var o)) orderEl = o;
                else if (root.TryGetProperty("payload", out var p)) orderEl = p;
                else if (root.TryGetProperty("data", out var d)) orderEl = d;
                if (orderEl.HasValue)
                {
                    dto = JsonSerializer.Deserialize<CreateOrderRequestDto>(orderEl.Value.GetRawText());
                    if (dto != null) return true;
                }
            }
            catch { }

            // 3. MessagePack base64
            try
            {
                var bytes = Convert.FromBase64String(payload);
                var options = MessagePackSerializerOptions.Standard
                    .WithResolver(ContractlessStandardResolver.Instance)
                    .WithCompression(MessagePackCompression.Lz4BlockArray);
                dto = MessagePackSerializer.Deserialize<CreateOrderRequestDto>(bytes, options);
                if (dto != null) return true;
            }
            catch { }

            error = "无法解析 payload";
            return false;
        }
    }
}
