using System.Security.Cryptography;
using Ato.Copilot.Core.Configuration.Auth;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Services.Auth;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Auth;

/// <summary>
/// Feature 051 T065 [US3] — RED-phase unit tests for
/// <see cref="RememberedTenantCookieService"/>. Asserts the wire format,
/// round-trip, and the "NEVER throws / always returns null on failure"
/// contract documented in <c>contracts/internal-services.md § 3</c> and
/// <c>research.md § R8</c>.
/// </summary>
public sealed class RememberedTenantCookieServiceTests
{
    private readonly string _signingKey;
    private readonly RememberedTenantCookieService _sut;

    public RememberedTenantCookieServiceTests()
    {
        // Stable 32-byte key for every test in this class. A second key is
        // generated in the "wrong key" test below.
        _signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        _sut = BuildSut(_signingKey);
    }

    [Fact]
    public void Issue_Produces_FourPart_Base64Url_String()
    {
        // Arrange
        var tenantId = Guid.NewGuid();

        // Act
        var cookie = _sut.Issue(tenantId, TimeSpan.FromMinutes(30));

        // Assert — 4 dot-separated parts.
        cookie.Should().NotBeNullOrWhiteSpace();
        var parts = cookie.Split('.');
        parts.Should().HaveCount(4,
            "research.md § R8 mandates {tenantId}.{iat}.{exp}.{hmac}");

        // Each part is base64url (no '+', '/', or '=' padding).
        foreach (var part in parts)
        {
            part.Should().NotBeNullOrEmpty();
            part.Should().NotContain("+");
            part.Should().NotContain("/");
            part.Should().NotContain("=");
        }
    }

    [Fact]
    public void Validate_RoundTrips_FreshlyIssuedCookie()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var cookie = _sut.Issue(tenantId, TimeSpan.FromMinutes(30));

        // Act
        var result = _sut.Validate(cookie);

        // Assert
        result.Should().Be(tenantId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Validate_ReturnsNull_WhenAnyOfFirstThreePartsMutated(int partIndex)
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var cookie = _sut.Issue(tenantId, TimeSpan.FromMinutes(30));
        var parts = cookie.Split('.');

        // Mutate one byte of the chosen part (flip the first character).
        var original = parts[partIndex];
        // Swap the first character predictably so the test does not depend
        // on randomness in the input alphabet.
        var first = original[0];
        var swapped = first == 'A' ? 'B' : 'A';
        parts[partIndex] = swapped + original.Substring(1);
        var tampered = string.Join('.', parts);

        // Act
        var result = _sut.Validate(tampered);

        // Assert
        result.Should().BeNull(
            "tampering with any signed component invalidates the HMAC");
    }

    [Fact]
    public void Validate_ReturnsNull_WhenHmacMutated()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var cookie = _sut.Issue(tenantId, TimeSpan.FromMinutes(30));
        var parts = cookie.Split('.');

        // Replace the HMAC segment with an obviously bogus value of the
        // same length so we hit the validation path, not a parse path.
        parts[3] = new string('Z', parts[3].Length);
        var tampered = string.Join('.', parts);

        // Act
        var result = _sut.Validate(tampered);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Validate_ReturnsNull_WhenExpired()
    {
        // Arrange — TTL=-1s ⇒ exp is in the past at issue time.
        var tenantId = Guid.NewGuid();
        var cookie = _sut.Issue(tenantId, TimeSpan.FromSeconds(-1));

        // Act
        var result = _sut.Validate(cookie);

        // Assert
        result.Should().BeNull("exp in the past must be rejected");
    }

    [Fact]
    public void Validate_ReturnsNull_WhenSignedWithDifferentKey()
    {
        // Arrange — fresh cookie signed with key A, validate with key B.
        var tenantId = Guid.NewGuid();
        var cookie = _sut.Issue(tenantId, TimeSpan.FromMinutes(30));

        var otherKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var otherSut = BuildSut(otherKey);

        // Act
        var result = otherSut.Validate(cookie);

        // Assert
        result.Should().BeNull(
            "rotating the signing key invalidates all outstanding cookies (R8)");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-a-cookie")]                                  // 1 part
    [InlineData("a.b")]                                           // 2 parts
    [InlineData("a.b.c")]                                         // 3 parts
    [InlineData("a.b.c.d.e")]                                     // 5 parts
    [InlineData("!!!.@@@.###.$$$")]                               // invalid base64
    [InlineData("AAAA.BBBB.CCCC.DDDD")]                           // valid base64 but wrong length payload
    public void Validate_ReturnsNull_OnMalformedInput_DoesNotThrow(string? input)
    {
        // Act
        Action act = () => _sut.Validate(input!);
        var result = _sut.Validate(input!);

        // Assert
        act.Should().NotThrow("contract: Validate NEVER throws");
        result.Should().BeNull();
    }

    private static RememberedTenantCookieService BuildSut(string base64Key)
    {
        var opts = Options.Create(new AuthOptions
        {
            Cookie = new AuthCookieOptions { SigningKey = base64Key },
        });
        return new RememberedTenantCookieService(opts);
    }
}
