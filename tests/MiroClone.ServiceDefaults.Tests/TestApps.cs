using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace MiroClone.ServiceDefaults.Tests;

/// <summary>Builds in-process hosts on TestServer. No socket is opened, so no test needs the network.</summary>
internal static class TestApps
{
    public static WebApplicationBuilder NewBuilder(IDictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        return builder;
    }
}

/// <summary>
/// A local authservice stand-in: serves an OIDC discovery document and a JWKS built from a freshly generated RSA key.
/// The API under test fetches both through its backchannel, so the real JWKS path is exercised.
/// </summary>
internal sealed class TestAuthority : IAsyncDisposable
{
    public const string Issuer = "https://auth.test";
    public const string KeyId = "test-key-1";

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

    /// <summary>Signs a token with this authority's key, so the JWKS entry matches it.</summary>
    public string IssueToken(string audience, Guid userId, Guid workspaceId, DateTime expiresUtc) =>
        TokenFactory.Rs256(SigningKey, KeyId, Issuer, audience, userId, workspaceId, expiresUtc);

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        SigningKey.Dispose();
    }
}

internal static class TokenFactory
{
    public static string Rs256(RSA key, string kid, string issuer, string audience, Guid userId, Guid workspaceId, DateTime expiresUtc)
    {
        var credentials = new SigningCredentials(new RsaSecurityKey(key) { KeyId = kid }, SecurityAlgorithms.RsaSha256);
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer,
            audience,
            [new("sub", userId.ToString()), new("workspace", workspaceId.ToString())],
            notBefore: null,
            expires: expiresUtc,
            signingCredentials: credentials);
        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>An HS256 token signed with a shared secret, for the algorithm-confusion test.</summary>
    public static string Hs256(string audience, Guid userId, Guid workspaceId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 32)));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            TestAuthority.Issuer,
            audience,
            [new("sub", userId.ToString()), new("workspace", workspaceId.ToString())],
            notBefore: null,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);
        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }
}
