using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Tenancy.Csp;

/// <summary>
/// T190 [US9]: unit tests for <c>CspCapabilityMappingService</c>
/// (FR-101, FR-102) — the wrapper around <see cref="ICapabilityMappingService"/>
/// that applies the operator-configured confidence threshold and produces
/// <see cref="CapabilityMappingResult"/> records suitable for the wizard
/// upload pipeline.
/// </summary>
/// <remarks>
/// <para>
/// RED until T204 lands the production
/// <c>Ato.Copilot.Agents.Services.Tenancy.CspCapabilityMappingService</c>
/// (or its eventual home in <c>Ato.Copilot.Core.Services.Tenancy</c>). The
/// SUT type is resolved via reflection from a small set of candidate fully
/// qualified names so this test class compiles cleanly without the
/// implementation existing.
/// </para>
/// <para>
/// Behavior matrix asserted:
/// <list type="bullet">
///   <item>confidence ≥ 0.6 → <c>Status = Mapped</c> with non-empty
///     <c>MappedNistControlIds</c>.</item>
///   <item>confidence &lt; 0.6 → <c>Status = NeedsReview</c> with
///     <c>MappingFailureReason = "Confidence below threshold (0.42)"</c>.</item>
///   <item>AI returns an empty list → <c>Status = NeedsReview</c>,
///     <c>MappedNistControlIds = []</c>,
///     <c>MappingFailureReason = "AI returned no candidate controls"</c>.</item>
///   <item>AI throws → <c>aiMappingAvailable = false</c>, capability list is
///     empty (component is preserved by the caller, not by this SUT).</item>
/// </list>
/// </para>
/// </remarks>
public class CspCapabilityMappingServiceTests
{
    private const double Threshold = 0.6d;

    [Fact]
    public async Task MapAsync_ConfidenceAboveThreshold_ReturnsMapped_WithControlIds()
    {
        // Arrange
        var ai = new Mock<ICapabilityMappingService>(MockBehavior.Strict);
        ai.Setup(x => x.MapAsync(It.IsAny<CapabilityMappingInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new CapabilityControlMatch("AC-2", "Account Management", 0.92, "rationale"),
                new CapabilityControlMatch("AC-2(1)", "Automated System Account Management", 0.81, "rationale"),
            });
        var component = NewComponent("Identity Provider");
        var sut = BuildSut(ai.Object);

        // Act
        var result = await sut.MapAsync(component, Threshold, CancellationToken.None);

        // Assert
        result.AiMappingAvailable.Should().BeTrue();
        result.AiMappingFailureReason.Should().BeNull();
        result.NeedsReview.Should().BeEmpty();
        result.Mapped.Should().HaveCount(1, "the SUT must collapse multiple AI candidates into one capability per component");
        var mapped = result.Mapped[0];
        mapped.Status.Should().Be(CspInheritedCapabilityStatus.Mapped);
        mapped.MappedNistControlIds.Should().BeEquivalentTo(new[] { "AC-2", "AC-2(1)" });
        mapped.MappingFailureReason.Should().BeNull();
        mapped.MappedBy.Should().Be(MappedBy.AI);
    }

    [Fact]
    public async Task MapAsync_ConfidenceBelowThreshold_ReturnsNeedsReview_WithFailureReason()
    {
        // Arrange
        var ai = new Mock<ICapabilityMappingService>(MockBehavior.Strict);
        ai.Setup(x => x.MapAsync(It.IsAny<CapabilityMappingInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new CapabilityControlMatch("AC-2", "Account Management", 0.42, "rationale"),
            });
        var component = NewComponent("Mystery Component");
        var sut = BuildSut(ai.Object);

        // Act
        var result = await sut.MapAsync(component, Threshold, CancellationToken.None);

        // Assert
        result.AiMappingAvailable.Should().BeTrue();
        result.Mapped.Should().BeEmpty();
        result.NeedsReview.Should().HaveCount(1);
        var capability = result.NeedsReview[0];
        capability.Status.Should().Be(CspInheritedCapabilityStatus.NeedsReview);
        capability.MappingFailureReason.Should().Be("Confidence below threshold (0.42)");
        capability.MappedBy.Should().Be(MappedBy.AI);
    }

    [Fact]
    public async Task MapAsync_AiReturnsEmpty_ReturnsNeedsReview_WithEmptyControlIds()
    {
        // Arrange
        var ai = new Mock<ICapabilityMappingService>(MockBehavior.Strict);
        ai.Setup(x => x.MapAsync(It.IsAny<CapabilityMappingInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CapabilityControlMatch>());
        var component = NewComponent("Unmappable Component");
        var sut = BuildSut(ai.Object);

        // Act
        var result = await sut.MapAsync(component, Threshold, CancellationToken.None);

        // Assert
        result.AiMappingAvailable.Should().BeTrue();
        result.Mapped.Should().BeEmpty();
        result.NeedsReview.Should().HaveCount(1);
        var capability = result.NeedsReview[0];
        capability.Status.Should().Be(CspInheritedCapabilityStatus.NeedsReview);
        capability.MappedNistControlIds.Should().BeEmpty();
        capability.MappingFailureReason.Should().Be("AI returned no candidate controls");
    }

    [Fact]
    public async Task MapAsync_AiThrows_ReturnsAiUnavailable_NoCapabilitiesCreated()
    {
        // Arrange
        var ai = new Mock<ICapabilityMappingService>(MockBehavior.Strict);
        ai.Setup(x => x.MapAsync(It.IsAny<CapabilityMappingInput>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("AI gateway down"));
        var component = NewComponent("Whatever Component");
        var sut = BuildSut(ai.Object);

        // Act
        var result = await sut.MapAsync(component, Threshold, CancellationToken.None);

        // Assert — FR-102: when AI is unreachable the upload SHOULD still
        // succeed (component preserved by the caller) and the result SHOULD
        // signal that no capabilities were auto-mapped.
        result.AiMappingAvailable.Should().BeFalse();
        result.AiMappingFailureReason.Should().NotBeNullOrEmpty();
        result.Mapped.Should().BeEmpty();
        result.NeedsReview.Should().BeEmpty();
    }

    // ───────────────────────────── Helpers ───────────────────────────────

    private static CspInheritedComponent NewComponent(string name) => new()
    {
        Id = Guid.NewGuid(),
        CspProfileId = Guid.NewGuid(),
        Name = name,
        Description = $"{name} description",
        ComponentType = CspComponentType.Service,
        SourceFormat = SourceFormat.Pdf,
        Status = CspInheritedComponentStatus.Draft,
        ImportedAt = DateTimeOffset.UtcNow,
        ImportedBy = "test",
    };

    /// <summary>
    /// Resolves the SUT type via reflection so this file compiles before T204
    /// has landed the implementation. When the implementation does not yet
    /// exist, the test fails with a clear message identifying the missing
    /// type — that is the RED state Constitution §VI requires us to observe.
    /// </summary>
    private static ICspCapabilityMappingService BuildSut(ICapabilityMappingService ai)
    {
        // Candidate fully qualified names. T204 may place the impl in either
        // Agents/Services/Tenancy or Core/Services/Tenancy — both are valid
        // homes per the reuse-first audit. The reflection lookup tolerates
        // either.
        var candidates = new[]
        {
            "Ato.Copilot.Agents.Services.Tenancy.CspCapabilityMappingService, Ato.Copilot.Agents",
            "Ato.Copilot.Core.Services.Tenancy.CspCapabilityMappingService, Ato.Copilot.Core",
        };

        Type? implType = null;
        foreach (var candidate in candidates)
        {
            implType = Type.GetType(candidate, throwOnError: false);
            if (implType is not null)
            {
                break;
            }
        }

        if (implType is null)
        {
            throw new NotImplementedException(
                "CspCapabilityMappingService is not yet implemented (pending T204). " +
                "RED state per Constitution §VI — implement the SUT to turn this test green.");
        }

        // Best-effort ctor selection: the SUT depends at minimum on
        // ICapabilityMappingService and a logger. We discover the ctor
        // dynamically so the test does not couple to a specific signature
        // that is still being designed.
        var ctor = Array.Find(implType.GetConstructors(),
            c => c.GetParameters().Any(p => p.ParameterType == typeof(ICapabilityMappingService)))
            ?? throw new InvalidOperationException(
                "CspCapabilityMappingService must accept ICapabilityMappingService in its constructor.");

        var args = ctor.GetParameters().Select(p =>
        {
            if (p.ParameterType == typeof(ICapabilityMappingService))
            {
                return (object?)ai;
            }
            // Logger<T>
            if (p.ParameterType.IsGenericType
                && p.ParameterType.GetGenericTypeDefinition().Name == "ILogger`1")
            {
                var loggerType = typeof(NullLogger<>).MakeGenericType(p.ParameterType.GetGenericArguments()[0]);
                return Activator.CreateInstance(loggerType);
            }
            // ILogger
            if (p.ParameterType.Name == "ILogger")
            {
                return NullLogger.Instance;
            }
            // Anything else: best effort — null for reference types,
            // default value for value types. Real DI will resolve them.
            return p.HasDefaultValue ? p.DefaultValue : null;
        }).ToArray();

        return (ICspCapabilityMappingService)ctor.Invoke(args)!;
    }
}
