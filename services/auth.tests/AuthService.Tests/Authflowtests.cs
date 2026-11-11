using AuthService.Data;
using AuthService.DTOs;
using AuthService.Services;
using Docker.DotNet.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthResponse = AuthService.DTOs.AuthResponse;

namespace AuthService.Tests;

[Collection("api")]
public sealed class AuthFlowTests
{
    private const string Password = "Passw0rd!123";
    private const string CookieName = "refresh_token";

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public AuthFlowTests(PostgresFixture fixture)
    {
        _factory = fixture.Factory;

        // Cookies are sent by hand so each test controls exactly which refresh token is presented.
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    // ---------- registration ----------

    [Fact]
    public async Task Register_ReturnsAccessToken_AndSetsHardenedRefreshCookie()
    {
        var response = await RegisterAsync(NewEmail());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.False(string.IsNullOrEmpty(body!.AccessToken));

        var cookie = response.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith($"{CookieName}="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        var email = NewEmail();
        await RegisterAsync(email);

        var second = await RegisterAsync(email);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Register_InvalidEmail_Returns400()
    {
        var response = await RegisterAsync("not-an-email");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- login ----------

    [Fact]
    public async Task Login_WithCorrectPassword_ReturnsTokens()
    {
        var email = NewEmail();
        await RegisterAsync(email);

        var response = await LoginAsync(email, Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrEmpty(GetRefreshCookie(response)));
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var email = NewEmail();
        await RegisterAsync(email);

        var response = await LoginAsync(email, "WrongPassw0rd!");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_Returns401()
    {
        var response = await LoginAsync(NewEmail(), Password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- refresh rotation ----------

    [Fact]
    public async Task Refresh_RotatesTheRefreshToken()
    {
        var first = GetRefreshCookie(await RegisterAsync(NewEmail()));

        var refreshed = await RefreshAsync(first);

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.NotEqual(first, GetRefreshCookie(refreshed));
    }

    [Fact]
    public async Task Refresh_WithoutCookie_Returns401()
    {
        var response = await SendAsync(HttpMethod.Post, "/api/auth/refresh");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_ReusingTokenWithinGracePeriod_StillSucceeds()
    {
        var first = GetRefreshCookie(await RegisterAsync(NewEmail()));
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(first)).StatusCode);

        // Same (already rotated) token again, immediately: looks like a parallel tab, not theft.
        var second = await RefreshAsync(first);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task Refresh_ReusingTokenAfterGracePeriod_RevokesAllSessions()
    {
        var first = GetRefreshCookie(await RegisterAsync(NewEmail()));
        var latest = GetRefreshCookie(await RefreshAsync(first));

        await AgeRevocationAsync(first); // pretend the rotation happened a minute ago

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(first)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(latest)).StatusCode);
    }

    // ---------- logout ----------

    [Fact]
    public async Task Logout_RevokesTheRefreshToken()
    {
        var first = GetRefreshCookie(await RegisterAsync(NewEmail()));

        var logout = await SendAsync(HttpMethod.Post, "/api/auth/logout", refreshCookie: first);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(first)).StatusCode);
    }

    // ---------- protected endpoint ----------

    [Fact]
    public async Task Me_WithAccessToken_Returns200()
    {
        var register = await RegisterAsync(NewEmail());
        var body = await register.Content.ReadFromJsonAsync<AuthResponse>();

        var me = await SendAsync(HttpMethod.Get, "/api/auth/me", accessToken: body!.AccessToken);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        var me = await SendAsync(HttpMethod.Get, "/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    // ---------- public key discovery ----------

    [Fact]
    public async Task Jwks_PublishesTheSigningKey()
    {
        var json = await _client.GetFromJsonAsync<JsonElement>("/.well-known/jwks.json");
        var key = json.GetProperty("keys")[0];

        Assert.Equal("RSA", key.GetProperty("kty").GetString());
        Assert.Equal("RS256", key.GetProperty("alg").GetString());
        Assert.False(string.IsNullOrEmpty(key.GetProperty("kid").GetString()));
        Assert.False(string.IsNullOrEmpty(key.GetProperty("n").GetString()));
    }

    [Fact]
    public async Task Discovery_PointsToJwks_AndReportsIssuer()
    {
        var json = await _client.GetFromJsonAsync<JsonElement>("/.well-known/openid-configuration");

        Assert.Equal(AuthApiFactory.Issuer, json.GetProperty("issuer").GetString());
        Assert.EndsWith("/.well-known/jwks.json", json.GetProperty("jwks_uri").GetString());
    }

    // ---------- helpers ----------

    private static string NewEmail() => $"{Guid.NewGuid():N}@test.cz";

    private Task<HttpResponseMessage> RegisterAsync(string email) =>
        _client.PostAsJsonAsync("/api/auth/register", new { email, password = Password });

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        _client.PostAsJsonAsync("/api/auth/login", new { email, password });

    private Task<HttpResponseMessage> RefreshAsync(string refreshCookie) =>
        SendAsync(HttpMethod.Post, "/api/auth/refresh", refreshCookie: refreshCookie);

    private Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        string? refreshCookie = null,
        string? accessToken = null)
    {
        var request = new HttpRequestMessage(method, url);

        if (refreshCookie is not null)
        {
            request.Headers.Add("Cookie", $"{CookieName}={refreshCookie}");
        }

        if (accessToken is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        }

        return _client.SendAsync(request);
    }

    // Returns the cookie value exactly as sent by the server (URL-encoded), ready to be sent back.
    private static string GetRefreshCookie(HttpResponseMessage response)
    {
        var header = response.Headers.GetValues("Set-Cookie").First(v => v.StartsWith($"{CookieName}="));
        return header.Split(';')[0][(CookieName.Length + 1)..];
    }

    // Moves the revocation time of a token into the past, so the grace period has "expired".
    private async Task AgeRevocationAsync(string cookieValue)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();

        var hash = tokens.HashToken(Uri.UnescapeDataString(cookieValue));
        var agedAt = (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-1);

        var updated = await db.RefreshTokens
            .Where(t => t.TokenHash == hash)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, agedAt));

        Assert.Equal(1, updated);
    }
}