using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace CryptoSpot.Application.DTOs.Users;

public class LoginRequestDto
{
    /// <summary>
    /// 邮箱或用户名（支持 username 和 emailOrUsername 两种字段名）
    /// </summary>
    [JsonPropertyName("username")]
    public string? Username { get; set; }
    
    [JsonPropertyName("emailOrUsername")]
    public string? EmailOrUsername { get; set; }
    
    /// <summary>
    /// 实际使用的登录标识（优先使用 EmailOrUsername，其次使用 Username）
    /// </summary>
    [JsonIgnore]
    public string LoginIdentifier => !string.IsNullOrEmpty(EmailOrUsername) ? EmailOrUsername : Username ?? string.Empty;

    [Required]
    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
    
    /// <summary>
    /// 验证登录标识是否为空
    /// </summary>
    public bool IsValid() => !string.IsNullOrEmpty(LoginIdentifier);
}
