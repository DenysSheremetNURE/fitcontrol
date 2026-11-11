using AuthService.Entities;

namespace AuthService.Tests;

public class RefreshTokenTests
{
    [Fact]
    public void Revoke_MarksTokenRevoked_AndStoresReplacement()
    {
        var token = new RefreshToken();

        token.Revoke("next-hash");

        Assert.True(token.IsRevoked);
        Assert.NotNull(token.RevokedAt);
        Assert.Equal("next-hash", token.ReplacedByTokenHash);
    }

    [Fact]
    public void Revoke_WithoutReplacement_LeavesReplacementEmpty()
    {
        var token = new RefreshToken();

        token.Revoke();

        Assert.True(token.IsRevoked);
        Assert.Null(token.ReplacedByTokenHash);
    }

    [Fact]
    public void Revoke_Twice_KeepsOriginalRevocationTimeAndReplacement()
    {
        var token = new RefreshToken();
        token.Revoke("first");
        var revokedAt = token.RevokedAt;

        token.Revoke("second");

        // This is what keeps the grace period from being extended by repeated calls.
        Assert.Equal(revokedAt, token.RevokedAt);
        Assert.Equal("first", token.ReplacedByTokenHash);
    }

    [Fact]
    public void IsExpired_IsTrueOnlyAfterExpiryTime()
    {
        var expired = new RefreshToken { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
        var valid = new RefreshToken { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1) };

        Assert.True(expired.IsExpired);
        Assert.False(valid.IsExpired);
    }

    [Fact]
    public void IsActive_IsFalseWhenRevokedOrExpired()
    {
        var active = new RefreshToken { ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) };
        var revoked = new RefreshToken { ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) };
        revoked.Revoke();
        var expired = new RefreshToken { ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) };

        Assert.True(active.IsActive);
        Assert.False(revoked.IsActive);
        Assert.False(expired.IsActive);
    }
}