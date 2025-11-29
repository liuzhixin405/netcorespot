using System.ComponentModel.DataAnnotations;

namespace CryptoSpot.Application.DTOs.Users;

public class LoginRequestDto
{
    [Required]
    public string EmailOrUsername { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
