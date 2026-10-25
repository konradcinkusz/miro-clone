using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace MiroClone.TestSupport;

/// <summary>
/// A local authservice stand-in: serves an OIDC discovery document and a JWKS built from a freshly generated RSA key.
/// The API under test fetches both through its backchannel, so the real JWKS path is exercised (ADR 0007).
/// </summary>
public sealed class TestAuthority : IAsyncDisposable
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
    public string IssueToken(string audience, Guid userId, IEnumerable<Guid> organizationIds, DateTime expiresUtc) =>
        TokenFactory.Rs256(SigningKey, KeyId, Issuer, audience, userId, organizationIds, expiresUtc);

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        SigningKey.Dispose();
    }
}
