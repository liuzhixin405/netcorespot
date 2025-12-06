using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace CryptoSpot.Application.DTOs.Auth
{
    public class LoginRequest
    {
        [JsonPropertyName("username")]
        public string? Username { get; set; }
        
        [JsonPropertyName("emailOrUsername")]
        public string? EmailOrUsername { get; set; }
        
        [JsonIgnore]
        public string LoginIdentifier => !string.IsNullOrEmpty(EmailOrUsername) ? EmailOrUsername : Username ?? string.Empty;
        
        [Required]
        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;
    }

    public class RegisterRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
        
        [Required]
        [MinLength(3)]
        public string Username { get; set; } = string.Empty;
        
        [Required]
        [MinLength(6)]
        public string Password { get; set; } = string.Empty;
    }

}
