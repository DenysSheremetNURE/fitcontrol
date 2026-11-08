
using System.ComponentModel.DataAnnotations;

namespace AuthService.DTOs;

public record RegisterRequest(
    [Required(ErrorMessage = "Email is required"),
     EmailAddress(ErrorMessage = "Invalid email format"),
     MaxLength(254, ErrorMessage = "Email is too long")]
    string Email,

    [Required(ErrorMessage = "Password is required"),
     MinLength(8, ErrorMessage = "Password must be at least 8 characters long"),
     MaxLength(128, ErrorMessage = "Password must be at most 128 characters long")]
    string Password
)
{
    public override string ToString() => $"RegisterRequest {{ Email = {Email} }}";
}

public record LoginRequest(
    [Required(ErrorMessage = "Email is required"),
     EmailAddress(ErrorMessage = "Invalid email format"),
     MaxLength(254, ErrorMessage = "Email is too long")]
    string Email,

    [Required(ErrorMessage = "Password is required"),
     MaxLength(128, ErrorMessage = "Password is too long")]
    string Password
)
{
    public override string ToString() => $"LoginRequest {{ Email = {Email} }}";
}

public record AuthResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt
)
{
    public override string ToString() => nameof(AuthResponse);
}