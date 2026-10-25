using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using MiroClone.TestSupport;

namespace MiroClone.BoardsService.Tests;

/// <summary>
/// Tokens for the boards tests, signed by the shared test authority. A workspace is one <c>organization</c> claim (ADR 0011);
/// a null workspace issues a token with no organization, which the service refuses as having no active workspace.
/// </summary>
internal static class BoardTokens
{
    public static string Issue(TestAuthority authority, Guid userId, Guid? workspaceId, string? name = null, DateTime? expiresUtc = null)
    {
        var claims = new List<Claim> { new("sub", userId.ToString()) };
        if (workspaceId is { } workspace)
        {
            claims.Add(new Claim("organization", workspace.ToString()));
        }

        if (name is not null)
        {
            claims.Add(new Claim("name", name));
        }

        var credentials = new SigningCredentials(
            new RsaSecurityKey(authority.SigningKey) { KeyId = TestAuthority.KeyId },
            SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            TestAuthority.Issuer,
            ServiceHost.Audience,
            claims,
            notBefore: null,
            expires: expiresUtc ?? DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
