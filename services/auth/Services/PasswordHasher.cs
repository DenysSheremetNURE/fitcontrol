using AuthService.Entities;
using Microsoft.AspNetCore.Identity;

namespace AuthService.Services;

public interface IPasswordHasher
{
    string HashPassword(User user, string password);
    PasswordVerificationResult VerifyPassword(User user, string hashedPassword, string providedPassword);
}

public sealed class PasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<User> _hasher = new();

    public string HashPassword(User user, string password) =>
        _hasher.HashPassword(user, password);

    public PasswordVerificationResult VerifyPassword(User user, string hashedPassword, string providedPassword) =>
        _hasher.VerifyHashedPassword(user, hashedPassword, providedPassword);
}