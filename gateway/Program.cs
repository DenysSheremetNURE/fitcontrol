using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
builder.Services.AddHealthChecks();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MetadataAddress = builder.Configuration["Jwt:MetadataAddress"]!;
        options.RequireHttpsMetadata = builder.Configuration.GetValue(
                "Jwt:RequireHttpsMetadata", !builder.Environment.IsDevelopment());
        options.MapInboundClaims = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(5),
            ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 }
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("authenticated", policy => policy.RequireAuthenticatedUser());

var app = builder.Build();

app.UseDefaultFiles();//
app.UseStaticFiles();//

app.MapHealthChecks("/health");

app.UseAuthentication();
app.UseAuthorization();

app.MapReverseProxy();

app.Run();