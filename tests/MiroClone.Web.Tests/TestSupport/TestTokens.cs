using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using MiroClone.ServiceDefaults;
using MiroClone.Web.Auth;

namespace MiroClone.Web.Tests.TestSupport;

/// <summary>Issues RS256 tokens shaped like authservice's (sub, email, organization claims) with a key only tests hold.</summary>
internal static class TestTokens
{
    public const string Issuer = "MiroClone";
    public const string Audience = "miro-clone";
    public const string Authority = "http://authority.test";

    private static readonly RSA Rsa = RSA.Create(2048);

    public static readonly RsaSecurityKey Key = new(Rsa) { KeyId = "test-key" };

    public static JwtValidationOptions ValidationOptions() => new()
    {
        Authority = Authority,
        Audience = Audience,
        Issuer = Issuer,
        RequireHttpsMetadata = false,
    };

    /// <summary>The discovery document the validator reads: the issuer and the one signing key.</summary>
    public static IConfigurationManager<OpenIdConnectConfiguration> Discovery()
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
        configuration.SigningKeys.Add(Key);
        return new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
    }

    public static string Issue(
        string userId = "11111111-1111-1111-1111-111111111111",
        IEnumerable<string>? organizations = null,
        TimeSpan? lifetime = null,
        string audience = Audience,
        bool requiresConsent = false)
    {
        var claims = new List<Claim>
        {
            new("sub", userId),
            new("email", "owner@dev.test"),
            new("email_verified", "true"),
        };
        foreach (var organization in organizations ?? [])
        {
            claims.Add(new Claim("organization", organization));
            claims.Add(new Claim($"organization:{organization}:role", "Owner"));
        }

        var token = new JwtSecurityToken(
            Issuer,
            audience,
            claims,
            notBefore: null,
            expires: DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromMinutes(15)),
            new SigningCredentials(Key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>The JSON authservice would return for a login or refresh, with the access token issued here.</summary>
    public static object TokenReply(string accessToken, string refreshToken = "refresh-1", int expiresIn = 900) =>
        new { accessToken, refreshToken, expiresIn, tokenType = "Bearer" };
}
