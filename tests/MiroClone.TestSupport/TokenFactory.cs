using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace MiroClone.TestSupport;

/// <summary>
/// Builds tokens shaped like authservice's session tokens (TokenService.BuildClaimsAsync): <c>sub</c>, <c>email_verified</c>,
/// one <c>organization</c> claim per membership and one <c>organization:{id}:role</c> claim per membership (ADR 0011).
/// </summary>
public static class TokenFactory
{
    public static string Rs256(
        RSA key,
        string kid,
        string issuer,
        string audience,
        Guid userId,
        IEnumerable<Guid> organizationIds,
        DateTime expiresUtc)
    {
        var credentials = new SigningCredentials(new RsaSecurityKey(key) { KeyId = kid }, SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(issuer, audience, SessionClaims(userId, organizationIds), notBefore: null, expires: expiresUtc, signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>An HS256 token signed with a shared secret, for the algorithm-confusion test.</summary>
    public static string Hs256(string issuer, string audience, Guid userId, IEnumerable<Guid> organizationIds)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 32)));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(issuer, audience, SessionClaims(userId, organizationIds), notBefore: null, expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static List<Claim> SessionClaims(Guid userId, IEnumerable<Guid> organizationIds)
    {
        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new("email", $"{userId:N}@example.test"),
            new("email_verified", "true", ClaimValueTypes.Boolean),
        };

        foreach (var organizationId in organizationIds)
        {
            claims.Add(new Claim("organization", organizationId.ToString()));
            claims.Add(new Claim($"organization:{organizationId}:role", "Member"));
        }

        return claims;
    }
}
