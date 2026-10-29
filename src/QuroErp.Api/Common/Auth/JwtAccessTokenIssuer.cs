using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using QuroErp.Application.Common.Auth;
using QuroErp.Domain.Common.Auth;

namespace QuroErp.Api.Common.Auth;

internal sealed class JwtAccessTokenIssuer(IOptions<JwtOptions> options) : IAccessTokenIssuer
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken Issue(AppUser user, bool mustChangePassword, DateTimeOffset now)
    {
        var jwt = options.Value;
        var expiresAt = now + jwt.Lifetime;
        var claims = new List<Claim>
        {
            new(AuthClaims.Subject, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(AuthClaims.Name, user.UserId),
            new(AuthClaims.TokenVersion, user.TokenVersion.ToString(CultureInfo.InvariantCulture)),
        };
        if (user.RoleCode is not null)
        {
            claims.Add(new Claim(AuthClaims.Role, user.RoleCode));
        }
        if (mustChangePassword)
        {
            claims.Add(new Claim(AuthClaims.MustChangePassword, AuthClaims.True));
        }

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(SigningKey(jwt), SecurityAlgorithms.HmacSha256),
        });
        return new AccessToken(token, expiresAt);
    }

    public static SymmetricSecurityKey SigningKey(JwtOptions jwt) => new(Encoding.UTF8.GetBytes(jwt.SigningKey));
}
