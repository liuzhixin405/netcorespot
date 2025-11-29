using System.ComponentModel.DataAnnotations;

namespace CryptoSpot.Application.DTOs.Users;

public class RegisterRequestDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(6)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [MinLength(3)]
    public string Username { get; set; } = string.Empty;
}
