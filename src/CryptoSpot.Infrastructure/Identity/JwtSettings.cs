namespace CryptoSpot.Infrastructure.Identity;

public sealed class JwtSettings
{
    public string SecretKey { get; init; } = string.Empty;
    public string Issuer { get; init; } = "CryptoSpot";
    public string Audience { get; init; } = "CryptoSpotUsers";
    public int ExpiryInDays { get; init; } = 7;
}
