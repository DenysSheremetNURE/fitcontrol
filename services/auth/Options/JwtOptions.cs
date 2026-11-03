using System.ComponentModel.DataAnnotations;

namespace AuthService.Options;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required(ErrorMessage = "Jwt:Issuer is required")]
    public string Issuer { get; set; } = string.Empty;

    [Required(ErrorMessage = "Jwt:Audience is required")]
    public string Audience { get; set; } = string.Empty;

    [Range(1, 1440, ErrorMessage = "AccessTokenExpirationMinutes should be between 1 and 1440 minutes")]
    public int AccessTokenExpirationMinutes { get; set; } = 15;

    [Range(1, 90, ErrorMessage = "RefreshTokenExpirationDays should be between 1 and 90 days")]
    public int RefreshTokenExpirationDays { get; set; } = 7;
}
