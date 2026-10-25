using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace MiroClone.ServiceDefaults;

/// <summary>Configuration under the "Auth" section. Only the authority is needed; no signing key is ever configured (P5).</summary>
public sealed class JwtValidationOptions
{
    public const string SectionName = "Auth";

    /// <summary>authservice base URL. Signing keys are read from its discovery document and JWKS.</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>Audience this service accepts. A token minted for another service is refused (ADR 0007).</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Expected issuer. When empty, the issuer the authority advertises is used.</summary>
    public string? Issuer { get; set; }

    /// <summary>Requires https for the authority. Turned off only for a local authority on http.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    internal void EnsureValid()
    {
        var problems = new List<string>();
        if (!Uri.TryCreate(Authority, UriKind.Absolute, out _))
        {
            problems.Add("Auth:Authority must be an absolute URL.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            problems.Add("Auth:Audience is required.");
        }

        if (ClockSkew < TimeSpan.Zero || ClockSkew > TimeSpan.FromMinutes(5))
        {
            problems.Add("Auth:ClockSkew must be between 0 and 5 minutes.");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join(' ', problems));
        }
    }
}

public static class AuthenticationExtensions
{
    /// <summary>
    /// Validates bearer tokens from authservice. Keys come from its JWKS, so rotation needs no redeploy (identity guide §10).
    /// Tokens must be RS256, carry this service's audience and be unexpired. Fails at startup on a bad configuration.
    /// </summary>
    /// <param name="configureBearer">Optional hook for tests and hosts, such as a backchannel handler. Applied last.</param>
    public static IServiceCollection AddJwtValidation(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<JwtBearerOptions>? configureBearer = null)
    {
        var settings = configuration.GetSection(JwtValidationOptions.SectionName).Get<JwtValidationOptions>()
            ?? new JwtValidationOptions();
        settings.EnsureValid();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = settings.Authority;
                options.RequireHttpsMetadata = settings.RequireHttpsMetadata;

                // Keep the claim names as issued ("sub", "workspace"), not the legacy .NET URIs.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = settings.Issuer,
                    ValidAudience = settings.Audience,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = settings.ClockSkew,
                };

                configureBearer?.Invoke(options);
            });

        services.AddAuthorization();
        return services;
    }
}
