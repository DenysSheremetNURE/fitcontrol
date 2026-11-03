namespace AuthService.Entities;

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TokenHash { get; set; } = string.Empty;
    public string? ReplacedByTokenHash { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsRevoked { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    public bool IsActive => !IsRevoked && !IsExpired;

    public void Revoke(string? replacedByTokenHash = null)
    {
        if (IsRevoked) return;
        IsRevoked = true;
        RevokedAt = DateTimeOffset.UtcNow;

        if (replacedByTokenHash != null)
        {
            ReplacedByTokenHash = replacedByTokenHash;
        }
    }
}