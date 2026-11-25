using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pitako.Modules.Security.Application.Abstractions;
using Pitako.Modules.Security.Domain;
using Pitako.SharedKernel.Time;

namespace Pitako.Modules.Security.Infrastructure.Crypto;

public sealed class JwtSettings
{
    public required string SigningKey { get; init; }
    public string Issuer { get; init; } = "pitako-saas";
    public string Audience { get; init; } = "pitako-api";
    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(30);
}

/// <summary>
/// Emissor e validador HMAC-SHA256 de tokens JWT contendo escopo (SaaS vs Loja), TenantId, SessionId e SecurityStamp.
/// </summary>
public sealed class HmacJwtTokenService : IAccessTokenService
{
    private readonly JwtSettings _settings;
    private readonly byte[] _keyBytes;
    private readonly IClock _clock;

    public HmacJwtTokenService(JwtSettings settings, IClock clock)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        if (string.IsNullOrWhiteSpace(settings.SigningKey) || settings.SigningKey.Length < 32)
            throw new ArgumentException("JwtSettings.SigningKey deve possuir ao menos 32 caracteres.", nameof(settings));

        _keyBytes = Encoding.UTF8.GetBytes(settings.SigningKey);
    }

    public IssuedAccessToken IssueAccessToken(User user, Guid sessionId)
    {
        var now = _clock.UtcNow;
        var expiresAt = now.Add(_settings.AccessTokenLifetime);

        var headerJson = JsonSerializer.Serialize(new { alg = "HS256", typ = "JWT" });
        var payloadJson = JsonSerializer.Serialize(new
        {
            iss = _settings.Issuer,
            aud = _settings.Audience,
            sub = user.Id.ToString(),
            sid = sessionId.ToString(),
            scope = user.Scope.ToString(),
            tenant_id = user.TenantId?.ToString(),
            sstamp = user.SecurityStamp,
            iat = now.ToUnixTimeSeconds(),
            exp = expiresAt.ToUnixTimeSeconds()
        });

        var headerEncoded = CryptoTokenGenerator.Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payloadEncoded = CryptoTokenGenerator.Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signingInput = $"{headerEncoded}.{payloadEncoded}";
        var signature = ComputeHmac(signingInput);

        return new IssuedAccessToken($"{signingInput}.{signature}", expiresAt);
    }

    public AccessTokenClaims? ValidateAccessToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var parts = token.Split('.');
        if (parts.Length != 3)
            return null;

        var signingInput = $"{parts[0]}.{parts[1]}";
        var expectedSignature = ComputeHmac(signingInput);

        byte[] actualSigBytes;
        byte[] expectedSigBytes;
        try
        {
            actualSigBytes = CryptoTokenGenerator.Base64UrlDecode(parts[2]);
            expectedSigBytes = CryptoTokenGenerator.Base64UrlDecode(expectedSignature);
        }
        catch (FormatException)
        {
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(actualSigBytes, expectedSigBytes))
            return null;

        try
        {
            var payloadBytes = CryptoTokenGenerator.Base64UrlDecode(parts[1]);
            using var doc = JsonDocument.Parse(payloadBytes);
            var root = doc.RootElement;

            if (root.GetProperty("iss").GetString() != _settings.Issuer ||
                root.GetProperty("aud").GetString() != _settings.Audience)
                return null;

            var expUnix = root.GetProperty("exp").GetInt64();
            if (DateTimeOffset.FromUnixTimeSeconds(expUnix) <= _clock.UtcNow)
                return null;

            var userId = Guid.Parse(root.GetProperty("sub").GetString()!);
            var sessionId = Guid.Parse(root.GetProperty("sid").GetString()!);
            var scope = Enum.Parse<UserScope>(root.GetProperty("scope").GetString()!);
            var tenantRaw = root.TryGetProperty("tenant_id", out var tidProp) && tidProp.ValueKind == JsonValueKind.String
                ? tidProp.GetString()
                : null;
            Guid? tenantId = string.IsNullOrWhiteSpace(tenantRaw) ? null : Guid.Parse(tenantRaw);
            var securityStamp = root.GetProperty("sstamp").GetString() ?? string.Empty;

            return new AccessTokenClaims(userId, sessionId, scope, tenantId, securityStamp);
        }
        catch
        {
            return null;
        }
    }

    private string ComputeHmac(string signingInput)
    {
        using var hmac = new HMACSHA256(_keyBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signingInput));
        return CryptoTokenGenerator.Base64UrlEncode(hash);
    }
}
