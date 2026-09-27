using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Workspaces;
using Ato.Copilot.Core.Services.ProviderAuthorizations;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Compliance;
using Microsoft.Extensions.Options;
using System.Threading.Channels;
using Moq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Services;

public sealed class ProviderDocumentLineageTests
{
    [Theory]
    [InlineData("SourceChanged")]
    [InlineData("WrongBaseline")]
    [InlineData("NoLongerCurrent")]
    public async Task CurrentWordDocument_DoesNotPresentMismatchedResponsibilityAsConfirmed(string state)
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        await fixture.SeedConfirmedDutyAsync();
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var confirmation = await db.Set<CapabilityResponsibilityConfirmation>().SingleAsync();
            if (state == "SourceChanged") confirmation.SourceRevision = "another-release";
            if (state == "WrongBaseline") confirmation.ReviewedBaselineId = "another-baseline";
            if (state == "NoLongerCurrent") confirmation.IsCurrent = false;
            await db.SaveChangesAsync();
        }

        // Act
        var document = await new DocumentTemplateService(fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DocumentTemplateService>.Instance).RenderDocxAsync("mission", "ssp");
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(document));
        using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
        var content = await reader.ReadToEndAsync();

        // Assert
        content.Should().NotContain("DEMO confirmed customer duty").And.Contain("Responsibility review gap")
            .And.NotContain("PRIVATE confirmation source");
    }

    [Theory]
    [InlineData("person")]
    [InlineData("organization")]
    public async Task GenericUpstreamExport_KeepsProviderIdentitySeparateFromReviewedAuthorityType(string issuerType)
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync(recordKind: "InheritedProviderReference", upstreamProvider: "Synthetic non-Microsoft SaaS", issuerType: issuerType);

        // Act
        var result = await fixture.ExportAsync();
        using var json = JsonDocument.Parse(result.OscalJson);
        var ssp = json.RootElement.GetProperty("system-security-plan");
        var source = ssp.GetProperty("system-implementation").GetProperty("leveraged-authorizations")[0];
        var issuerId = source.GetProperty("party-uuid").GetString();
        var issuer = ssp.GetProperty("metadata").GetProperty("parties").EnumerateArray()
            .Single(item => item.GetProperty("uuid").GetString() == issuerId);

        // Assert
        issuer.GetProperty("type").GetString().Should().Be(issuerType);
        issuer.GetProperty("name").GetString().Should().Be("DEMO Review Authority");
        source.GetProperty("props").EnumerateArray().Should().Contain(item =>
            item.GetProperty("name").GetString() == "upstream-provider" && item.GetProperty("value").GetString() == "Synthetic non-Microsoft SaaS");
        source.GetProperty("props").EnumerateArray().Should().Contain(item =>
            item.GetProperty("name").GetString() == "record-kind" && item.GetProperty("value").GetString() == "InheritedProviderReference");
    }

    [Fact]
    public async Task GenericUpstreamName_DoesNotRepairMissingReviewedAuthorityType()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync("MissingIssuerType", recordKind: "InheritedProviderReference", upstreamProvider: "Synthetic Company");

        // Act
        var result = await fixture.ExportAsync();
        using var json = JsonDocument.Parse(result.OscalJson);

        // Assert
        json.RootElement.GetProperty("system-security-plan").GetProperty("system-implementation")
            .TryGetProperty("leveraged-authorizations", out _).Should().BeFalse();
        result.Warnings.Should().Contain(warning => warning.Contains("authority type"));
    }

    [Fact]
    public async Task FinalEmassExport_DoesNotDiscardMissingSourceMetadataFinding()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync("MissingDate");
        var factory = fixture.Services.GetRequiredService<IServiceScopeFactory>();
        var service = new EmassExportService(factory, NullLogger<EmassExportService>.Instance,
            new OscalSspExportService(factory, NullLogger<OscalSspExportService>.Instance));

        // Act
        var action = () => service.ExportOscalAsync("mission", OscalModelType.Ssp);

        // Assert
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Provider authorization*");
    }

    [Fact]
    public async Task Export_UsesReviewedPinnedDecision_AndResolvableStableIssuer()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        await fixture.SeedConfirmedDutyAsync();

        // Act
        var first = await fixture.ExportAsync();
        var second = await fixture.ExportAsync();
        using var json = JsonDocument.Parse(first.OscalJson);
        using var repeated = JsonDocument.Parse(second.OscalJson);
        var ssp = json.RootElement.GetProperty("system-security-plan");
        var leveraged = ssp.GetProperty("system-implementation").GetProperty("leveraged-authorizations")[0];

        // Assert
        leveraged.GetProperty("title").GetString().Should().Be("DEMO Shared Services Decision");
        leveraged.GetProperty("date-authorized").GetString().Should().Be("2025-04-17");
        var issuerId = leveraged.GetProperty("party-uuid").GetString();
        var party = ssp.GetProperty("metadata").GetProperty("parties").EnumerateArray()
            .Single(p => p.GetProperty("uuid").GetString() == issuerId);
        party.GetProperty("name").GetString().Should().Be("DEMO Review Authority");
        var props = leveraged.GetProperty("props").EnumerateArray()
            .ToDictionary(p => p.GetProperty("name").GetString()!, p => p.GetProperty("value").GetString());
        props["decision-as-stated"].Should().Be("ATO");
        props["release-id"].Should().Be(fixture.Adoption.ReleaseId.ToString());
        props["adoption-id"].Should().Be(fixture.Adoption.Id.ToString());
        props["decision-revision-id"].Should().Be(fixture.Decision.Id.ToString());
        leveraged.GetProperty("remarks").GetString().Should().Contain("DEMO customer duty remains");
        repeated.RootElement.GetProperty("system-security-plan").GetProperty("system-implementation")
            .GetProperty("leveraged-authorizations")[0].GetProperty("party-uuid").GetString().Should().Be(issuerId);
        first.OscalJson.Should().NotContain("private/source.pdf");
        var factory = fixture.Services.GetRequiredService<IServiceScopeFactory>();
        var template = new DocumentTemplateService(factory, NullLogger<DocumentTemplateService>.Instance);
        var sspService = new SspService(factory, NullLogger<SspService>.Instance);
        var channel = Channel.CreateUnbounded<SspExportJob>();
        var exportService = new SspExportService(factory, sspService, template,
            new OscalSspExportService(factory, NullLogger<OscalSspExportService>.Instance),
            Mock.Of<IOscalSchemaValidationService>(), Mock.Of<ISspExportNotifier>(), NullLogger<SspExportService>.Instance,
            Options.Create(new ExportSettings { DataPath = fixture.OutputPath }), channel);
        var wordExport = await exportService.EnqueueExportAsync("mission", "docx", null, "DEMO reviewer");
        await exportService.ProcessExportAsync(await channel.Reader.ReadAsync());
        (await exportService.GetExportAsync(wordExport.Id))!.Status.Should().Be("Completed");
        var file = await exportService.GetExportFileStreamAsync(wordExport.Id);
        await using var fileStream = file!.Value.Stream!;
        using var archive = new System.IO.Compression.ZipArchive(fileStream, System.IO.Compression.ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
        var document = await reader.ReadToEndAsync();
        document.Should().Contain("DEMO Shared Services Decision").And.Contain("DEMO Review Authority")
            .And.Contain("2025-04-17").And.Contain(fixture.Adoption.ReleaseId.ToString())
            .And.Contain("DEMO confirmed customer duty").And.Contain("Provider evidence access gap")
            .And.NotContain("private/source.pdf").And.NotContain("PRIVATE confirmation source");
        var sspDocument = await sspService.GenerateSspAsync("mission");
        sspDocument.Content.Should().Contain("DEMO Shared Services Decision").And.Contain("DEMO confirmed customer duty")
            .And.Contain("Provider evidence access gap").And.NotContain("private/source.pdf");
        var streamed = new List<string>();
        await foreach (var section in sspService.StreamSspSectionsAsync("mission")) streamed.Add(section.Content);
        string.Join("\n", streamed).Should().Contain("DEMO confirmed customer duty").And.Contain(fixture.Adoption.ReleaseId.ToString());
        var pdf = await template.RenderPdfAsync("mission", "ssp");
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Theory]
    [InlineData("MissingDate")]
    [InlineData("MissingTitle")]
    [InlineData("MissingIssuer")]
    [InlineData("MissingIssuerType")]
    [InlineData("Unreviewed")]
    [InlineData("Tampered")]
    [InlineData("WrongTenant")]
    [InlineData("WrongProvider")]
    [InlineData("Inactive")]
    [InlineData("MissingContext")]
    [InlineData("MissingRelease")]
    [InlineData("Withdrawn")]
    [InlineData("ConflictingPins")]
    [InlineData("UnselectedHistory")]
    public async Task Export_InvalidOrInapplicableProvenance_DoesNotInventAuthority(string state)
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync(state);

        // Act
        var result = await fixture.ExportAsync();
        using var json = JsonDocument.Parse(result.OscalJson);

        // Assert
        json.RootElement.GetProperty("system-security-plan").GetProperty("system-implementation")
            .TryGetProperty("leveraged-authorizations", out _).Should().BeFalse();
        if (state is not ("WrongTenant" or "Inactive"))
            result.Warnings.Should().Contain(w => w.Contains("Provider authorization"));
    }

    [Fact]
    public async Task Export_SuccessorDraft_DoesNotReplacePinnedDecision()
    {
        // Arrange
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.Add(new ProviderAuthorizationRevision
            {
                ProviderId = fixture.Decision.ProviderId, OfferingId = fixture.Decision.OfferingId,
                RecordId = fixture.Decision.RecordId, SnapshotJson = "{\"reference\":\"DRAFT MUST NOT LEAK\"}",
                MetadataReviewState = "Unconfirmed", Revision = 2
            });
            await db.SaveChangesAsync();
        }

        // Act
        var result = await fixture.ExportAsync();

        // Assert
        result.OscalJson.Should().Contain("DEMO Shared Services Decision").And.NotContain("DRAFT MUST NOT LEAK");
    }

    private sealed class Fixture : IDisposable
    {
        public ServiceProvider Services { get; }
        public CapabilityAdoptionSnapshot Adoption { get; } = new();
        public ProviderAuthorizationRevision Decision { get; } = new();
        public string OutputPath { get; } = Path.Combine(Directory.GetCurrentDirectory(), $"provider-format-tests-{Guid.NewGuid():N}");
        private readonly Guid tenant = Guid.NewGuid();

        public Fixture()
        {
            var name = Guid.NewGuid().ToString();
            Services = new ServiceCollection().AddDbContext<AtoCopilotContext>(o => o.UseInMemoryDatabase(name))
                .BuildServiceProvider();
        }

        public async Task SeedAsync(string? state = null, string recordKind = "ProviderDecision",
            string? upstreamProvider = null, string issuerType = "organization")
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            db.Add(new RegisteredSystem { Id = "mission", TenantId = tenant, Name = "DEMO mission", Acronym = "DEMO" });
            Decision.ProviderId = Guid.NewGuid();
            Decision.OfferingId = Guid.NewGuid();
            Decision.RecordId = Guid.NewGuid();
            Decision.BoundaryRevisionId = Guid.NewGuid();
            var source = new CreateProviderDecisionRequest(1, Decision.BoundaryRevisionId, [], recordKind,
                state == "MissingTitle" ? "" : "DEMO Shared Services Decision",
                state == "MissingIssuer" ? null : "DEMO Review Authority", "ATO",
                state == "MissingDate" ? null : "2025-04-17", null, null, "NoExpiryStated", "DEMO reviewed scope",
                ["DEMO customer duty remains"], [new(Guid.NewGuid(), Guid.NewGuid(), "private/source.pdf", "p1", "DEMO private quote")])
                { IssuingAuthorityType = state == "MissingIssuerType" ? null : issuerType, UpstreamProvider = upstreamProvider };
            Decision.SnapshotJson = ProviderAuthorizationStore.Json(source);
            Decision.SnapshotHash = ProviderAuthorizationStore.Hash(Decision.SnapshotJson);
            Decision.MetadataReviewState = state == "Unreviewed" ? "Unconfirmed" : "Recorded";
            Decision.RecordedBy = "demo-reviewer";
            Decision.RecordedAt = DateTimeOffset.UtcNow;
            var context = new ProviderCatalogContextSnapshot
            {
                ProviderId = Decision.ProviderId, OfferingId = Decision.OfferingId,
                ReleaseId = Guid.NewGuid(), CapabilityId = Guid.NewGuid()
            };
            context.SnapshotJson = ProviderAuthorizationStore.Json(new ProviderPublicationContextMaterial(
                context.OfferingId, 1, Decision.BoundaryRevisionId, "boundary-hash", null, null,
                [new(Decision.RecordId, Decision.Id, Decision.SnapshotHash, "lifecycle-hash")], [], []));
            context.SnapshotHash = ProviderAuthorizationStore.Hash(context.SnapshotJson);
            var release = new ProviderCapabilityRelease
            {
                Id = context.ReleaseId.Value, CapabilityId = context.CapabilityId.Value, Revision = 1,
                SnapshotHash = "DEMO-working-snapshot-hash", SnapshotJson = "{}", PublishedBy = "demo-reviewer"
            };
            Adoption.ProviderId = Decision.ProviderId;
            Adoption.OfferingId = Decision.OfferingId;
            Adoption.TenantId = state == "WrongTenant" ? Guid.NewGuid() : tenant;
            Adoption.SystemId = "mission";
            Adoption.SubscriptionId = "subscription";
            Adoption.CapabilityId = context.CapabilityId.Value;
            Adoption.ReleaseId = context.ReleaseId.Value;
            Adoption.ContextSnapshotId = context.Id;
            Adoption.SnapshotJson = ProviderAuthorizationStore.Json(new
            {
                Request = new AdoptProviderCapabilityRequest(Adoption.AssignmentId, Adoption.AssignmentRevision,
                    Adoption.CapabilityId, Adoption.ReleaseId, context.SnapshotHash, "preview-hash"),
                Selected = new { ReleaseSnapshotHash = release.SnapshotHash, ReleaseRevision = release.Revision,
                    Applicability = new ProviderSnapshotRef(context.Id, context.Revision, context.SnapshotHash) }
            });
            Adoption.SnapshotHash = ProviderAuthorizationStore.Hash(Adoption.SnapshotJson);
            if (state == "Tampered") Decision.SnapshotJson += " ";
            if (state == "WrongProvider") Decision.ProviderId = Guid.NewGuid();
            db.AddRange(Decision, Adoption, new CapabilitySubscription
            {
                Id = Adoption.SubscriptionId, RegisteredSystemId = "mission", RoutingTenantId = tenant,
                CspInheritedCapabilityId = Adoption.CapabilityId.ToString(), IsActive = state != "Inactive",
                CurrentAdoptionSnapshotId = state is "ConflictingPins" or "UnselectedHistory" ? null : Adoption.Id,
                AdoptionSelectionRevision = 1
            });
            if (state != "MissingContext") db.Add(context);
            if (state != "MissingRelease") db.Add(release);
            if (state == "Withdrawn")
                db.Add(new ProviderAuthorizationLifecycleEvent
                {
                    ProviderId = Decision.ProviderId, OfferingId = Decision.OfferingId, RecordId = Decision.RecordId,
                    AuthorizationRevisionId = Decision.Id, Kind = "Withdrawn", EffectiveOn = "2025-05-01"
                });
            if (state == "ConflictingPins")
                db.Add(new CapabilityAdoptionSnapshot
                {
                    ProviderId = Adoption.ProviderId, OfferingId = Adoption.OfferingId,
                    TenantId = tenant, SystemId = "mission", SubscriptionId = Adoption.SubscriptionId,
                    ReleaseId = Guid.NewGuid(), ContextSnapshotId = Guid.NewGuid()
                });
            await db.SaveChangesAsync();
        }

        public Task<OscalExportResult> ExportAsync() => new OscalSspExportService(
            Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<OscalSspExportService>.Instance).ExportAsync("mission");

        public async Task SeedConfirmedDutyAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
            var baseline = new ControlBaseline
            {
                TenantId = tenant, RegisteredSystemId = "mission", BaselineLevel = "Moderate", ControlIds = ["AU-2"], TotalControls = 1
            };
            var release = await db.ProviderCapabilityReleases.SingleAsync();
            db.AddRange(baseline, new CapabilityResponsibilityConfirmation
            {
                TenantId = tenant, RegisteredSystemId = "mission", SubscriptionId = Adoption.SubscriptionId,
                ReviewedBaselineId = baseline.Id, ControlId = "AU-2", SourceRevision = release.SnapshotHash,
                SourceSnapshotJson = "PRIVATE confirmation source", InheritanceType = InheritanceType.Shared,
                Provider = "DEMO provider", CustomerResponsibility = "DEMO confirmed customer duty",
                ConfirmedBy = "DEMO ISSM", IsCurrent = true
            });
            await db.SaveChangesAsync();
        }

        public void Dispose()
        {
            Services.Dispose();
            if (Directory.Exists(OutputPath)) Directory.Delete(OutputPath, true);
        }
    }
}
