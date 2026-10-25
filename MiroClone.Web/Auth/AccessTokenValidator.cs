using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using MiroClone.ServiceDefaults;

namespace MiroClone.Web.Auth;

/// <summary>
/// Validates an authservice access token when the host takes it in (blazor-web-app §7: the host validates the identity
/// provider's tokens when the session is issued). The rules match the services' (ADR 0007, ADR 0011): RS256, the
/// estate issuer and audience, unexpired, keys from authservice's discovery document. A token that fails is never stored.
/// </summary>
public sealed class AccessTokenValidator(JwtValidationOptions options, IConfigurationManager<OpenIdConnectConfiguration> discovery)
{
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };

    /// <summary>The validator for the configured authority: keys come from its discovery document, fetched and cached.</summary>
    public static IConfigurationManager<OpenIdConnectConfiguration> DiscoveryFor(JwtValidationOptions options) =>
        new ConfigurationManager<OpenIdConnectConfiguration>(
            options.Authority.TrimEnd('/') + "/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = options.RequireHttpsMetadata });

    /// <summary>Returns the claims of a valid token, with claim names as issued ("sub", "organization").</summary>
    /// <exception cref="SecurityTokenException">The token is not valid for this estate.</exception>
    public async Task<ClaimsPrincipal> ValidateAsync(string accessToken, CancellationToken ct = default)
    {
        var configuration = await discovery.GetConfigurationAsync(ct);
        var parameters = new TokenValidationParameters
        {
            ValidIssuer = options.Issuer ?? configuration.Issuer,
            ValidAudience = options.Audience,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = configuration.SigningKeys,
            ClockSkew = options.ClockSkew,
        };

        return _handler.ValidateToken(accessToken, parameters, out _);
    }
}
