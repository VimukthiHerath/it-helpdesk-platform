using Auth.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth.Api.Controller;

[ApiController]
[AllowAnonymous]
public class JwksController : ControllerBase
{
    private readonly IRsaKeyService _rsaKeyService;
    private readonly IConfiguration _configuration;

    public JwksController(IRsaKeyService rsaKeyService, IConfiguration configuration)
    {
        _rsaKeyService = rsaKeyService;
        _configuration = configuration;
    }

    [HttpGet("/.well-known/jwks.json")]
    [Produces("application/json")]
    public IActionResult GetJwks()
    {
        var jwks = _rsaKeyService.GetPublicJwks();
        return Ok(jwks);
    }

    [HttpGet("/.well-known/openid-configuration")]
    [Produces("application/json")]
    public IActionResult GetOpenIdConfiguration()
    {
        var issuer = _configuration["Jwt:Issuer"] ?? "AuthApi";
        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        var openIdConfig = new
        {
            issuer = issuer,
            jwks_uri = $"{baseUrl}/.well-known/jwks.json",
            id_token_signing_alg_values_supported = new[] { "RS256" },
            response_types_supported = new[] { "token", "id_token" },
            subject_types_supported = new[] { "public" }
        };

        return Ok(openIdConfig);
    }
}
