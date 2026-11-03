using System.Security.Claims;
using AuthService.Data;
using AuthService.DTOs;
using AuthService.Entities;
using AuthService.Options;
using AuthService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AuthService.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController : ControllerBase
{
    private const string RefreshCookieName = "refresh_token";
    private static readonly TimeSpan RotationGrace = TimeSpan.FromSeconds(10);

    private readonly AppDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        AppDbContext dbContext,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IOptions<JwtOptions> jwtOptions,
        ILogger<AuthController> logger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        if (await _dbContext.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken))
        {
            return Conflict(new { message = "User with this email already exists." });
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            Role = "User",
            CreatedAt = DateTimeOffset.UtcNow
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
        _dbContext.Users.Add(user);

        var issued = IssueTokens(user);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Conflict(new { message = "User with this email already exists." });
        }

        SetRefreshCookie(issued.RawRefreshToken, issued.RefreshExpiresAt);
        return Ok(issued.Response);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user == null)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var verificationResult = _passwordHasher.VerifyPassword(user, user.PasswordHash, request.Password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
            _logger.LogInformation("Password hash updated for user {UserId} due to algorithm upgrade.", user.Id);
        }

        var issued = IssueTokens(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        SetRefreshCookie(issued.RawRefreshToken, issued.RefreshExpiresAt);
        return Ok(issued.Response);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(RefreshCookieName, out var rawToken) || string.IsNullOrEmpty(rawToken))
        {
            return Unauthorized(new { message = "Missing refresh token." });
        }

        var incomingTokenHash = _tokenService.HashToken(rawToken);

        var existingToken = await _dbContext.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.TokenHash == incomingTokenHash, cancellationToken);

        if (existingToken == null)
        {
            ClearRefreshCookie();
            return Unauthorized(new { message = "Invalid refresh token." });
        }

        var withinGrace = existingToken.IsRevoked
            && existingToken.ReplacedByTokenHash != null
            && existingToken.RevokedAt is { } revokedAt
            && DateTimeOffset.UtcNow - revokedAt < RotationGrace;

        if (existingToken.IsRevoked && !withinGrace)
        {
            if (existingToken.ReplacedByTokenHash != null)
            {
                _logger.LogWarning("Reuse attempt detected for rotated refresh token of user {UserId}.", existingToken.UserId);

                var activeTokens = await _dbContext.RefreshTokens
                    .Where(r => r.UserId == existingToken.UserId && !r.IsRevoked)
                    .ToListAsync(cancellationToken);

                foreach (var token in activeTokens)
                {
                    token.Revoke();
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            ClearRefreshCookie();
            return Unauthorized(new { message = "Invalid refresh token." });
        }

        if (existingToken.IsExpired)
        {
            ClearRefreshCookie();
            return Unauthorized(new { message = "Refresh token expired. Please log in again." });
        }

        var newRawRefreshToken = _tokenService.GenerateRefreshToken();
        var newRefreshTokenHash = _tokenService.HashToken(newRawRefreshToken);

        existingToken.Revoke(newRefreshTokenHash);

        var newRefreshToken = CreateRefreshTokenEntity(existingToken.UserId, newRefreshTokenHash);
        _dbContext.RefreshTokens.Add(newRefreshToken);

        var newAccessToken = _tokenService.GenerateAccessToken(existingToken.User);
        await _dbContext.SaveChangesAsync(cancellationToken);

        SetRefreshCookie(newRawRefreshToken, newRefreshToken.ExpiresAt);

        return Ok(new AuthResponse(
            newAccessToken,
            DateTimeOffset.UtcNow.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes)
        ));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (Request.Cookies.TryGetValue(RefreshCookieName, out var rawToken) && !string.IsNullOrEmpty(rawToken))
        {
            var tokenHash = _tokenService.HashToken(rawToken);
            var token = await _dbContext.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == tokenHash, cancellationToken);

            if (token != null)
            {
                token.Revoke();
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        ClearRefreshCookie();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    [DisableRateLimiting]
    public IActionResult GetCurrentUser()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = User.FindFirstValue(ClaimTypes.Email);
        var role = User.FindFirstValue(ClaimTypes.Role);

        return Ok(new
        {
            Id = userId,
            Email = email,
            Role = role
        });
    }

    private sealed record IssuedTokens(AuthResponse Response, string RawRefreshToken, DateTimeOffset RefreshExpiresAt);

    private IssuedTokens IssueTokens(User user)
    {
        var accessToken = _tokenService.GenerateAccessToken(user);
        var rawRefreshToken = _tokenService.GenerateRefreshToken();
        var refreshTokenHash = _tokenService.HashToken(rawRefreshToken);

        var refreshToken = CreateRefreshTokenEntity(user.Id, refreshTokenHash);
        _dbContext.RefreshTokens.Add(refreshToken);

        var response = new AuthResponse(
            accessToken,
            DateTimeOffset.UtcNow.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes));

        return new IssuedTokens(response, rawRefreshToken, refreshToken.ExpiresAt);
    }

    private RefreshToken CreateRefreshTokenEntity(Guid userId, string tokenHash)
    {
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static CookieOptions CookieOpts(DateTimeOffset? expires = null) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth",
        Expires = expires
    };

    private void SetRefreshCookie(string rawToken, DateTimeOffset expires) =>
        Response.Cookies.Append(RefreshCookieName, rawToken, CookieOpts(expires));

    private void ClearRefreshCookie() =>
        Response.Cookies.Delete(RefreshCookieName, CookieOpts());
}