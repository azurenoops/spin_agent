using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;
using Ato.Copilot.Core.Onboarding;
using Ato.Copilot.Mcp.Endpoints.Onboarding;

namespace Ato.Copilot.Tests.Integration.Onboarding;

/// <summary>
/// T047 — Foundational endpoint contract tests. Verifies that the
/// Constitution VII envelope helper used by every wizard endpoint
/// returns the right HTTP status for the auth-forbidden code (403)
/// and the conventional 400 for every other wizard error code.
///
/// Per-step, full end-to-end integration coverage lives in the
/// per-user-story integration test files (T048+).
/// </summary>
public class WizardEnvelopeContractTests
{
    [Fact]
    public void Failure_WithAuthForbiddenCode_ReturnsHttp403()
    {
        var result = (IStatusCodeHttpResult)CallFailure(
            WizardErrorCodes.AuthForbidden,
            "denied",
            suggestion: "elevate");

        result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Theory]
    [InlineData(nameof(WizardErrorCodes.JobFailed))]
    [InlineData(nameof(WizardErrorCodes.BootstrapRace))]
    [InlineData(nameof(WizardErrorCodes.EmassInvalidFormat))]
    [InlineData(nameof(WizardErrorCodes.SspPdfNoTextLayer))]
    [InlineData(nameof(WizardErrorCodes.TemplateWrongFormat))]
    [InlineData(nameof(WizardErrorCodes.QuotaExceeded))]
    public void Failure_WithNonAuthCode_ReturnsHttp400(string codeFieldName)
    {
        var code = (string)typeof(WizardErrorCodes)
            .GetField(codeFieldName, BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

        var result = (IStatusCodeHttpResult)CallFailure(code, "boom");

        result.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void Failure_PayloadHasOkFalseAndAllFields()
    {
        var result = (IValueHttpResult)CallFailure(
            WizardErrorCodes.JobFailed,
            "boom",
            "retry from the wizard");

        var payload = result.Value!;
        var props = payload.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(payload));
        props["ok"].Should().Be(false);
        props["errorCode"].Should().Be(WizardErrorCodes.JobFailed);
        props["message"].Should().Be("boom");
        props["suggestion"].Should().Be("retry from the wizard");
    }

    /// <summary>Invoke the public <see cref="Envelope.Failure(string, string, string?)"/>.</summary>
    private static IResult CallFailure(string errorCode, string message, string? suggestion = null)
        => Envelope.Failure(errorCode, message, suggestion);
}
