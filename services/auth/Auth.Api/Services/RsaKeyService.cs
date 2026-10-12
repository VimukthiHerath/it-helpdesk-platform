using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Api.Services;

public interface IRsaKeyService
{
    RsaSecurityKey GetSigningKey();
    JsonWebKeySet GetPublicJwks();
    string KeyId { get; }
}

public class RsaKeyService : IRsaKeyService
{
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _signingKey;
    private readonly JsonWebKeySet _jwks;
    private readonly string _keyId;

    public string KeyId => _keyId;

    public RsaKeyService(IConfiguration configuration, IWebHostEnvironment environment, ILogger<RsaKeyService> logger)
    {
        _keyId = configuration["Jwt:KeyId"] ?? "auth-api-key-1";

        var configuredPem = configuration["Jwt:RsaPrivateKeyPem"];
        var keyPath = configuration["Jwt:RsaKeyPath"];

        if (!string.IsNullOrWhiteSpace(configuredPem))
        {
            logger.LogInformation("Loading RSA private key from configuration/environment variable.");
            _rsa = RSA.Create();
            _rsa.ImportFromPem(configuredPem.ToCharArray());
        }
        else
        {
            // Check filesystem or default keys folder
            var defaultKeyPath = Path.Combine(environment.ContentRootPath, "keys", "jwt-rsa-private.pem");
            var resolvedPath = !string.IsNullOrWhiteSpace(keyPath) ? keyPath : defaultKeyPath;

            if (File.Exists(resolvedPath))
            {
                logger.LogInformation("Loading RSA private key from file: {Path}", resolvedPath);
                var pemContent = File.ReadAllText(resolvedPath);
                _rsa = RSA.Create();
                _rsa.ImportFromPem(pemContent.ToCharArray());
            }
            else
            {
                logger.LogInformation("No existing RSA key found. Generating new 2048-bit RSA key pair for local development.");
                _rsa = RSA.Create(2048);

                try
                {
                    var dir = Path.GetDirectoryName(resolvedPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    var pem = _rsa.ExportPkcs8PrivateKeyPem();
                    File.WriteAllText(resolvedPath, pem);
                    logger.LogInformation("Persisted generated RSA private key to {Path}", resolvedPath);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not persist RSA private key to disk. Key will exist in-memory only.");
                }
            }
        }

        _signingKey = new RsaSecurityKey(_rsa)
        {
            KeyId = _keyId
        };

        // Export only public parameters for JWKS
        var publicParams = _rsa.ExportParameters(false);
        var jwk = new JsonWebKey
        {
            Kty = JsonWebAlgorithmsKeyTypes.RSA,
            Use = "sig",
            Kid = _keyId,
            Alg = SecurityAlgorithms.RsaSha256,
            N = Base64UrlEncoder.Encode(publicParams.Modulus),
            E = Base64UrlEncoder.Encode(publicParams.Exponent)
        };

        _jwks = new JsonWebKeySet();
        _jwks.Keys.Add(jwk);
    }

    public RsaSecurityKey GetSigningKey() => _signingKey;

    public JsonWebKeySet GetPublicJwks() => _jwks;
}
