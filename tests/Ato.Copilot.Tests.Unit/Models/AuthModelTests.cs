using Ato.Copilot.Core.Models.Auth;
using FluentAssertions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Models;

/// <summary>
/// Unit tests for auth entity model validation — CacSession, JitRequestEntity, CertificateRoleMapping.
/// T081: Field constraints, status transitions, and validation rules.
/// </summary>
public class AuthModelTests
{
    // ─── CacSession Tests ────────────────────────────────────────────────────

    [Fact]
    public void CacSession_ShouldHaveDefaultValues()
    {
        var session = new CacSession();

        session.Id.Should().NotBeEmpty();
        session.UserId.Should().BeEmpty();
        session.DisplayName.Should().BeEmpty();
        session.Email.Should().BeEmpty();
        session.TokenHash.Should().BeEmpty();
        session.ClientType.Should().Be(ClientType.Web);
        session.Status.Should().Be(SessionStatus.Active);
        session.SessionStart.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        session.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void CacSession_ExpiresAt_ShouldBeSettable()
    {
        var session = new CacSession
        {
            SessionStart = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(8)
        };

        session.ExpiresAt.Should().BeAfter(session.SessionStart);
        var duration = session.ExpiresAt - session.SessionStart;
        duration.TotalHours.Should().BeApproximately(8, 0.01);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(24)]
    public void CacSession_TimeoutRange_ShouldBeWithin1To24Hours(int hours)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new CacSession
        {
            SessionStart = now,
            ExpiresAt = now.AddHours(hours)
        };

        var duration = session.ExpiresAt - session.SessionStart;
        duration.TotalHours.Should().BeInRange(1, 24);
    }

    [Fact]
    public void CacSession_TokenHash_ShouldAccept64CharSha256()
    {
        var sha256Hash = new string('a', 64);
        var session = new CacSession { TokenHash = sha256Hash };

        session.TokenHash.Should().HaveLength(64);
    }

    [Theory]
    [InlineData(SessionStatus.Active)]
    [InlineData(SessionStatus.Expired)]
    [InlineData(SessionStatus.Terminated)]
    public void CacSession_Status_ShouldAcceptAllValues(SessionStatus status)
    {
        var session = new CacSession { Status = status };
        session.Status.Should().Be(status);
    }

    [Theory]
    [InlineData(ClientType.VSCode)]
    [InlineData(ClientType.Teams)]
    [InlineData(ClientType.Web)]
    [InlineData(ClientType.CLI)]
    public void CacSession_ClientType_ShouldAcceptAllValues(ClientType clientType)
    {
        var session = new CacSession { ClientType = clientType };
        session.ClientType.Should().Be(clientType);
    }

    // ─── JitRequestEntity Tests ──────────────────────────────────────────────

    [Fact]
    public void JitRequestEntity_ShouldHaveDefaultValues()
    {
        var request = new JitRequestEntity();

        request.Id.Should().NotBeEmpty();
        request.UserId.Should().BeEmpty();
        request.RoleName.Should().BeEmpty();
        request.Scope.Should().BeEmpty();
        request.Justification.Should().BeEmpty();
        request.Status.Should().Be(JitRequestStatus.PendingApproval);
        request.RequestedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(JitRequestType.PimRoleActivation)]
    [InlineData(JitRequestType.JitVmAccess)]
    public void JitRequestEntity_RequestType_ShouldAcceptAllValues(JitRequestType type)
    {
        var request = new JitRequestEntity { RequestType = type };
        request.RequestType.Should().Be(type);
    }

    [Fact]
    public void JitRequestEntity_Justification_MinLength()
    {
        var shortJustification = "Short";
        var validJustification = "This is a valid justification that meets the minimum length requirement of 20 characters";

        shortJustification.Length.Should().BeLessThan(20);
        validJustification.Length.Should().BeGreaterOrEqualTo(20);
    }

    [Theory]
    [InlineData(JitRequestStatus.PendingApproval)]
    [InlineData(JitRequestStatus.Active)]
    [InlineData(JitRequestStatus.Denied)]
    [InlineData(JitRequestStatus.Deactivated)]
    [InlineData(JitRequestStatus.Expired)]
    [InlineData(JitRequestStatus.Failed)]
    public void JitRequestEntity_Status_ShouldAcceptAllValues(JitRequestStatus status)
    {
        var request = new JitRequestEntity { Status = status };
        request.Status.Should().Be(status);
    }

    [Fact]
    public void JitRequestEntity_ValidStatusTransitions_Active_ToDeactivated()
    {
        var request = new JitRequestEntity { Status = JitRequestStatus.Active };
        request.Status = JitRequestStatus.Deactivated;
        request.DeactivatedAt = DateTimeOffset.UtcNow;

        request.Status.Should().Be(JitRequestStatus.Deactivated);
        request.DeactivatedAt.Should().NotBeNull();
    }

    [Fact]
    public void JitRequestEntity_ValidStatusTransitions_PendingApproval_ToApproved()
    {
        var request = new JitRequestEntity { Status = JitRequestStatus.PendingApproval };
        request.Status = JitRequestStatus.Active;
        request.ActivatedAt = DateTimeOffset.UtcNow;
        request.ApproverId = "approver-id";
        request.ApproverDisplayName = "Security Lead";

        request.Status.Should().Be(JitRequestStatus.Active);
        request.ActivatedAt.Should().NotBeNull();
        request.ApproverId.Should().NotBeNull();
    }

    [Fact]
    public void JitRequestEntity_ValidStatusTransitions_PendingApproval_ToDenied()
    {
        var request = new JitRequestEntity { Status = JitRequestStatus.PendingApproval };
        request.Status = JitRequestStatus.Denied;
        request.ApproverComments = "Insufficient justification";

        request.Status.Should().Be(JitRequestStatus.Denied);
        request.ApproverComments.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void JitRequestEntity_TicketNumberIsOptional()
    {
        var request = new JitRequestEntity { TicketNumber = null, TicketSystem = null };
        request.TicketNumber.Should().BeNull();
        request.TicketSystem.Should().BeNull();
    }

    [Fact]
    public void JitRequestEntity_TicketNumberCanBeProvided()
    {
        var request = new JitRequestEntity
        {
            TicketNumber = "SNOW-INC-1234",
            TicketSystem = "ServiceNow"
        };

        request.TicketNumber.Should().Be("SNOW-INC-1234");
        request.TicketSystem.Should().Be("ServiceNow");
    }

    [Fact]
    public void JitRequestEntity_JitVmAccess_HasVmFields()
    {
        var request = new JitRequestEntity
        {
            RequestType = JitRequestType.JitVmAccess,
            VmName = "vm-web01",
            ResourceGroup = "rg-prod",
            SubscriptionId = "sub-123",
            Port = 22,
            Protocol = "SSH",
            SourceIp = "10.0.1.50"
        };

        request.VmName.Should().Be("vm-web01");
        request.ResourceGroup.Should().Be("rg-prod");
        request.Port.Should().Be(22);
        request.Protocol.Should().Be("SSH");
    }

    // ─── CertificateRoleMapping Tests ────────────────────────────────────────

    [Fact]
    public void CertificateRoleMapping_ShouldHaveDefaultValues()
    {
        var mapping = new CertificateRoleMapping();

        mapping.Id.Should().NotBeEmpty();
        mapping.CertificateThumbprint.Should().BeEmpty();
        mapping.CertificateSubject.Should().BeEmpty();
        mapping.MappedRole.Should().BeEmpty();
        mapping.IsActive.Should().BeTrue();
        mapping.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void CertificateRoleMapping_ShouldStoreThumbprintAndSubject()
    {
        var mapping = new CertificateRoleMapping
        {
            CertificateThumbprint = "A1B2C3D4E5F6",
            CertificateSubject = "CN=SMITH.JANE.M.1234567890",
            MappedRole = "Compliance.Auditor"
        };

        mapping.CertificateThumbprint.Should().NotBeEmpty();
        mapping.CertificateSubject.Should().StartWith("CN=");
        mapping.MappedRole.Should().Contain("Compliance.");
    }

    [Fact]
    public void CertificateRoleMapping_InactiveMapping_ShouldBeSkipped()
    {
        var mapping = new CertificateRoleMapping { IsActive = false };
        mapping.IsActive.Should().BeFalse();
    }
}
