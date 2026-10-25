using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.IdentityModel.Tokens;

namespace MiroClone.BoardsService.Tests;

/// <summary>
/// A local authservice stand-in: serves an OIDC discovery document and a JWKS built from a freshly generated RSA key.
/// The API under test fetches both through its backchannel, so the real JWKS path runs. Same shape as the kernel's
/// test authority (tests/MiroClone.ServiceDefaults.Tests/TestApps.cs), copied because test projects do not share code.
/// </summary>
internal sealed class TestAuthority : IAsyncDisposable
{
    public const string Issuer = "https://auth.test";
    private const string KeyId = "test-key-1";

    private readonly WebApplication _app;

    private TestAuthority(WebApplication app, RSA signingKey)
    {
        _app = app;
        SigningKey = signingKey;
    }

    public RSA SigningKey { get; }

    public HttpMessageHandler Backchannel => _app.GetTestServer().CreateHandler();

    public static async Task<TestAuthority> StartAsync()
    {
        var signingKey = RSA.Create(2048);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();

        var parameters = signingKey.ExportParameters(includePrivateParameters: false);
        var jwk = new
        {
            kty = "RSA",
            kid = KeyId,
            use = "sig",
            alg = "RS256",
            n = Base64UrlEncoder.Encode(parameters.Modulus!),
            e = Base64UrlEncoder.Encode(parameters.Exponent!),
        };

        app.MapGet("/.well-known/openid-configuration", () => Results.Json(new { issuer = Issuer, jwks_uri = $"{Issuer}/jwks" }));
        app.MapGet("/jwks", () => Results.Json(new { keys = new[] { jwk } }));

        await app.StartAsync();
        return new TestAuthority(app, signingKey);
    }

    /// <summary>Signs an RS256 token with this authority's key. A null workspace produces a token with no active workspace.</summary>
    public string IssueToken(Guid userId, Guid? workspaceId, string? name = null, DateTime? expiresUtc = null)
    {
        var claims = new List<Claim> { new("sub", userId.ToString()) };
        if (workspaceId is { } workspace)
        {
            claims.Add(new Claim("workspace", workspace.ToString()));
        }

        if (name is not null)
        {
            claims.Add(new Claim("name", name));
        }

        var credentials = new SigningCredentials(new RsaSecurityKey(SigningKey) { KeyId = KeyId }, SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            Issuer,
            ServiceHost.Audience,
            claims,
            notBefore: null,
            expires: expiresUtc ?? DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        SigningKey.Dispose();
    }
}
