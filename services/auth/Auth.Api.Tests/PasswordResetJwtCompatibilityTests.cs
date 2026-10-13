using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Auth.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Auth.Api.Tests;

public class PasswordResetJwtCompatibilityTests
{
    [Fact]
    public void LoginTokenKeepsRs256AndContainsTheCurrentSecurityStamp()
    {
        using var keys = new TestKeys();
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "AuthApi",
            ["Jwt:Audience"] = "ItHelpdeskClient"
        }).Build();
        var service = new TokenService(keys, settings);
        var stamp = Guid.NewGuid().ToString();

        var token = service.CreateToken(42, "qa@example.com", "Employee", stamp);
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        Assert.Equal(SecurityAlgorithms.RsaSha256, jwt.Header.Alg);
        Assert.Equal(keys.KeyId, jwt.Header.Kid);

        var principal = handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "AuthApi",
            ValidateAudience = true,
            ValidAudience = "ItHelpdeskClient",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = keys.GetSigningKey()
        }, out _);
        Assert.Contains(principal.Claims, claim => claim.Type == "SecurityStamp" && claim.Value == stamp);
    }

    private sealed class TestKeys : IRsaKeyService, IDisposable
    {
        private readonly RSA _rsa = RSA.Create(2048);
        public string KeyId => "qa-test-key";
        public RsaSecurityKey GetSigningKey() => new(_rsa) { KeyId = KeyId };
        public JsonWebKeySet GetPublicJwks() => new();
        public void Dispose() => _rsa.Dispose();
    }
}
