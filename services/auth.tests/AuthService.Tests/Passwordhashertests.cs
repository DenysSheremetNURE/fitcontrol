using AuthService.Entities;
using AuthService.Services;
using Microsoft.AspNetCore.Identity;

namespace AuthService.Tests;

public class PasswordHasherTests
{
    private const string Password = "Passw0rd!123";

    private readonly PasswordHasher _hasher = new();
    private readonly User _user = new();

    [Fact]
    public void CorrectPassword_Verifies()
    {
        var hash = _hasher.HashPassword(_user, Password);

        Assert.Equal(PasswordVerificationResult.Success, _hasher.VerifyPassword(_user, hash, Password));
    }

    [Fact]
    public void WrongPassword_Fails()
    {
        var hash = _hasher.HashPassword(_user, Password);

        Assert.Equal(PasswordVerificationResult.Failed, _hasher.VerifyPassword(_user, hash, "WrongPassw0rd!"));
    }

    [Fact]
    public void Hash_DoesNotContainThePlainPassword()
    {
        var hash = _hasher.HashPassword(_user, Password);

        Assert.DoesNotContain(Password, hash);
    }

    [Fact]
    public void SamePassword_ProducesDifferentHashes_BecauseOfRandomSalt()
    {
        var first = _hasher.HashPassword(_user, Password);
        var second = _hasher.HashPassword(_user, Password);

        Assert.NotEqual(first, second);
    }
}