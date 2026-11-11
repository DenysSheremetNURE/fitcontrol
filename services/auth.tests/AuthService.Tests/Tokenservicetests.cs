using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using AuthService.Entities;
using AuthService.Options;
using AuthService.Services;
using AuthService.Services.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace AuthService.Tests;

public sealed class TokenServiceTests : IDisposable
{
    private const string Issuer = "test-issuer";
    private const string Audience = "test-audience";

    private readonly TestKeyManager _keys = new();
    private readonly TokenService _service;

    public TokenServiceTests()
    {
        // The fully qualified name is needed: "Options" alone resolves to the AuthService.Options namespace.
        var options = Microsoft.Extensions.Options.Options.Create(new JwtOptions
        {
            Issuer = Issuer,
            Audience = Audience,
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7
        });

        _service = new TokenService(_keys, options);
    }

    [Fact]
    public void AccessToken_ContainsExpectedClaimsAndHeader()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "a@b.cz", Role = "Admin" };

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(_service.GenerateAccessToken(user));

        Assert.Equal("RS256", jwt.Header.Alg);
        Assert.Equal(TestKeyManager.KeyId, jwt.Header.Kid);
        Assert.Equal(Issuer, jwt.Issuer);
        Assert.Contains(Audience, jwt.Audiences);
        Assert.Equal(user.Id.ToString(), jwt.Subject);
        Assert.Equal("a@b.cz", jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.Equal("Admin", jwt.Claims.First(c => c.Type == "role").Value);
    }

    [Fact]
    public void AccessToken_IsVerifiableWithThePublicKeyOnly()
    {
        var token = _service.GenerateAccessToken(NewUser());

        new JwtSecurityTokenHandler().ValidateToken(
            token,
            ValidationParameters(_keys.GetPublicKey()),
            out var validated);

        Assert.NotNull(validated);
    }

    [Fact]
    public void AccessToken_VerifiedWithAnotherKey_IsRejected()
    {
        var token = _service.GenerateAccessToken(NewUser());
        using var otherRsa = RSA.Create(2048);
        var otherKey = new RsaSecurityKey(otherRsa) { KeyId = TestKeyManager.KeyId };

        Assert.ThrowsAny<SecurityTokenException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(token, ValidationParameters(otherKey), out _));
    }

    [Fact]
    public void RefreshToken_IsRandomAnd64BytesLong()
    {
        var first = _service.GenerateRefreshToken();
        var second = _service.GenerateRefreshToken();

        Assert.NotEqual(first, second);
        Assert.Equal(64, Convert.FromBase64String(first).Length);
    }

    [Fact]
    public void HashToken_IsDeterministicLowercaseSha256Hex()
    {
        var hash = _service.HashToken("abc");

        Assert.Equal(_service.HashToken("abc"), hash);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
    }

    public void Dispose() => _keys.Dispose();

    private static User NewUser() => new() { Id = Guid.NewGuid(), Email = "a@b.cz", Role = "User" };

    private static TokenValidationParameters ValidationParameters(SecurityKey key) => new()
    {
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = key,
        ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
        ClockSkew = TimeSpan.Zero
    };

    // Throwaway in-memory key pair, so the test does not touch the file system.
    private sealed class TestKeyManager : IRsaKeyManager, IDisposable
    {
        public const string KeyId = "test-key";

        private readonly RSA _rsa = RSA.Create(2048);
        private readonly RsaSecurityKey _publicKey;
        private readonly SigningCredentials _signingCredentials;

        public TestKeyManager()
        {
            _publicKey = new RsaSecurityKey(_rsa.ExportParameters(includePrivateParameters: false)) { KeyId = KeyId };
            _signingCredentials = new SigningCredentials(
                new RsaSecurityKey(_rsa) { KeyId = KeyId },
                SecurityAlgorithms.RsaSha256);
        }

        public SigningCredentials GetSigningCredentials() => _signingCredentials;

        public RsaSecurityKey GetPublicKey() => _publicKey;

        public void Dispose() => _rsa.Dispose();
    }
}