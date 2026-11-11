using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestPlatform.TestHost;
using Testcontainers.PostgreSql;

namespace AuthService.Tests;

/// <summary>
/// Hosts the real AuthService in-process, wired to a throwaway PostgreSQL container.
/// </summary>
public sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "fitcontrol-test";
    public const string Audience = "fitcontrol-test-clients";

    private readonly string _connectionString;
    private readonly string _keysDirectory =
        Path.Combine(Path.GetTempPath(), "auth-tests-" + Guid.NewGuid().ToString("N"));

    public AuthApiFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:Audience", Audience);
        builder.UseSetting("Jwt:AccessTokenExpirationMinutes", "15");
        builder.UseSetting("Jwt:RefreshTokenExpirationDays", "7");
        builder.UseSetting("RsaKeys:KeysPath", _keysDirectory);
        builder.UseSetting("RsaKeys:AllowKeyGeneration", "true");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("RateLimiting:AuthPermitLimit", "100000");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(_keysDirectory))
        {
            Directory.Delete(_keysDirectory, recursive: true);
        }
    }
}

/// <summary>
/// One PostgreSQL container and one application instance shared by all integration tests.
/// Tests stay independent because each one registers its own unique e-mail address.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public AuthApiFactory Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        Factory = new AuthApiFactory(_postgres.GetConnectionString());
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<PostgresFixture>
{
}