using System.Security.Cryptography;
using Ato.Copilot.Core.Configuration.Auth;
using Ato.Copilot.Core.Interfaces.Auth;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Core.Services.Auth;

/// <summary>
/// Feature 051 US3 / FR-012 — HMAC-SHA256 implementation of
/// <see cref="IRememberedTenantCookieService"/>. Per
/// <c>research.md § R8</c>:
/// <list type="bullet">
///   <item>Cookie wire format:
///     <c>base64url(tenantId-16B).base64url(iatMillis-8B).base64url(expMillis-8B).base64url(hmac-32B)</c>.
///   </item>
///   <item>HMAC is over <c>tenantId || iat || exp</c> (concatenated raw bytes).</item>
///   <item>Signing key sourced from <c>AuthOptions.Cookie.SigningKey</c>
///     (base64-encoded 32-byte secret).</item>
/// </list>
/// </summary>
/// <remarks>
/// Constant-time HMAC comparison is enforced via
/// <see cref="CryptographicOperations.FixedTimeEquals"/>. <see cref="Validate"/>
/// never throws — any failure path returns <see langword="null"/>.
/// </remarks>
public sealed class RememberedTenantCookieService : IRememberedTenantCookieService
{
    private const int TenantIdBytes = 16;
    private const int TimestampBytes = 8;
    private const int HmacBytes = 32;

    private readonly byte[] _signingKey;

    public RememberedTenantCookieService(IOptions<AuthOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var raw = options.Value.Cookie.SigningKey ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            // In Development the validator allows an empty key; surface a
            // deterministic placeholder so Issue still produces a valid
            // 4-part cookie. In Production AuthOptionsValidator would have
            // failed startup, so we never reach here outside dev/test.
            _signingKey = new byte[32];
        }
        else
        {
            try
            {
                _signingKey = Convert.FromBase64String(raw);
            }
            catch (FormatException)
            {
                // Tolerate raw-string keys for development convenience —
                // mirrors TenantImpersonationService's tolerant path.
                _signingKey = System.Text.Encoding.UTF8.GetBytes(raw);
            }
        }

        if (_signingKey.Length < 32)
        {
            var padded = new byte[32];
            Buffer.BlockCopy(_signingKey, 0, padded, 0, _signingKey.Length);
            _signingKey = padded;
        }
    }

    /// <inheritdoc />
    public string Issue(Guid tenantId, TimeSpan ttl)
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var expMs = nowMs + (long)ttl.TotalMilliseconds;

        var tenantBytes = tenantId.ToByteArray(); // 16 bytes
        var iatBytes = BitConverter.GetBytes(nowMs); // 8 bytes (host endianness)
        var expBytes = BitConverter.GetBytes(expMs);

        var payload = new byte[TenantIdBytes + TimestampBytes + TimestampBytes];
        Buffer.BlockCopy(tenantBytes, 0, payload, 0, TenantIdBytes);
        Buffer.BlockCopy(iatBytes, 0, payload, TenantIdBytes, TimestampBytes);
        Buffer.BlockCopy(expBytes, 0, payload, TenantIdBytes + TimestampBytes, TimestampBytes);

        var hmac = HMACSHA256.HashData(_signingKey, payload);

        return string.Join('.',
            ToBase64Url(tenantBytes),
            ToBase64Url(iatBytes),
            ToBase64Url(expBytes),
            ToBase64Url(hmac));
    }

    /// <inheritdoc />
    public Guid? Validate(string? cookieValue)
    {
        if (string.IsNullOrWhiteSpace(cookieValue))
        {
            return null;
        }

        try
        {
            var parts = cookieValue.Split('.');
            if (parts.Length != 4)
            {
                return null;
            }

            var tenantBytes = FromBase64Url(parts[0]);
            var iatBytes = FromBase64Url(parts[1]);
            var expBytes = FromBase64Url(parts[2]);
            var hmac = FromBase64Url(parts[3]);

            if (tenantBytes is null || iatBytes is null || expBytes is null || hmac is null)
            {
                return null;
            }

            if (tenantBytes.Length != TenantIdBytes ||
                iatBytes.Length != TimestampBytes ||
                expBytes.Length != TimestampBytes ||
                hmac.Length != HmacBytes)
            {
                return null;
            }

            // Recompute expected HMAC.
            var payload = new byte[TenantIdBytes + TimestampBytes + TimestampBytes];
            Buffer.BlockCopy(tenantBytes, 0, payload, 0, TenantIdBytes);
            Buffer.BlockCopy(iatBytes, 0, payload, TenantIdBytes, TimestampBytes);
            Buffer.BlockCopy(expBytes, 0, payload, TenantIdBytes + TimestampBytes, TimestampBytes);

            var expected = HMACSHA256.HashData(_signingKey, payload);
            if (!CryptographicOperations.FixedTimeEquals(expected, hmac))
            {
                return null;
            }

            // Expiry check.
            var expMs = BitConverter.ToInt64(expBytes, 0);
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (expMs <= nowMs)
            {
                return null;
            }

            return new Guid(tenantBytes);
        }
        catch
        {
            // Contract: Validate NEVER throws. Any unexpected exception
            // (FormatException, ArgumentException, IndexOutOfRange, etc.)
            // becomes a soft rejection.
            return null;
        }
    }

    private static string ToBase64Url(byte[] bytes)
    {
        var b64 = Convert.ToBase64String(bytes);
        return b64
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[]? FromBase64Url(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var b64 = value.Replace('-', '+').Replace('_', '/');
        switch (b64.Length % 4)
        {
            case 2: b64 += "=="; break;
            case 3: b64 += "="; break;
            case 1: return null; // invalid base64 length
        }
        try
        {
            return Convert.FromBase64String(b64);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
