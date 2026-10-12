using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Api.Services;

public interface ITokenService
{
    string CreateToken(int userId, string email, string role, string securityStamp, int expiryMinutes = 60);
}

public class TokenService : ITokenService
{
    private readonly IRsaKeyService _rsaKeyService;
    private readonly IConfiguration _configuration;

    public TokenService(IRsaKeyService rsaKeyService, IConfiguration configuration)
    {
        _rsaKeyService = rsaKeyService;
        _configuration = configuration;
    }

    public string CreateToken(int userId, string email, string role, string securityStamp, int expiryMinutes = 60)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(securityStamp);
        var issuer = _configuration["Jwt:Issuer"] ?? "AuthApi";
        var audience = _configuration["Jwt:Audience"] ?? "ItHelpdeskClient";

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Role, role),
            new("SecurityStamp", securityStamp),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var signingCredentials = new SigningCredentials(
            _rsaKeyService.GetSigningKey(),
            SecurityAlgorithms.RsaSha256
        );

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(expiryMinutes),
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = signingCredentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}
