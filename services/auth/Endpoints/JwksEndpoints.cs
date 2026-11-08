using AuthService.Options;
using AuthService.Services.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AuthService.Endpoints;

public static class JwksEndpoints
{
    public static IEndpointRouteBuilder MapJwksEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/.well-known/jwks.json", (IRsaKeyManager rsaKeyManager, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "public, max-age=300";

            var publicKey = rsaKeyManager.GetPublicKey();
            var rsaParameters = publicKey.Parameters;

            var jwk = new
            {
                kty = "RSA",
                use = "sig",
                alg = SecurityAlgorithms.RsaSha256,
                kid = publicKey.KeyId,
                n = Base64UrlEncoder.Encode(rsaParameters.Modulus!),
                e = Base64UrlEncoder.Encode(rsaParameters.Exponent!)
            };

            return Results.Ok(new { keys = new[] { jwk } });
        })
        .AllowAnonymous();

        endpoints.MapGet("/.well-known/openid-configuration", (HttpContext context, IOptions<JwtOptions> jwtOptions) =>
        {
            context.Response.Headers.CacheControl = "public, max-age=300";

            var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";

            return Results.Ok(new
            {
                issuer = jwtOptions.Value.Issuer,
                jwks_uri = $"{baseUrl}/.well-known/jwks.json",
                id_token_signing_alg_values_supported = new[] { "RS256" }
            });
        })
        .AllowAnonymous();

        return endpoints;
    }
}