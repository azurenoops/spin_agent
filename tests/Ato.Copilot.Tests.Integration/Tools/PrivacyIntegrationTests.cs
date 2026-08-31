using System.Diagnostics;
using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Agents.Compliance.Tools;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ato.Copilot.Tests.Integration.Tools;

/// <summary>
/// Integration tests for Feature 021 — Privacy + Interconnection end-to-end flows and performance.
/// T055 + T062.
/// </summary>
public class PrivacyIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly PrivacyService _privacyService;
    private readonly InterconnectionService _interconnectionService;
    private readonly RmfLifecycleService _lifecycleService;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public PrivacyIntegrationTests()
    {
        var dbName = $"PrivacyIntTest_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<AtoCopilotContext>(opts =>
            opts.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped);
        services.AddDbContextFactory<AtoCopilotContext>(opts =>
            opts.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped);
        services.AddLogging();

        _serviceProvider = services.BuildServiceProvider();
        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

        _privacyService = new PrivacyService(scopeFactory, Mock.Of<ILogger<PrivacyService>>());
        _interconnectionService = new InterconnectionService(scopeFactory, Mock.Of<ILogger<InterconnectionService>>());
        _lifecycleService = new RmfLifecycleService(scopeFactory, _serviceProvider.GetRequiredService<IDbContextFactory<AtoCopilotContext>>(), Mock.Of<ILogger<RmfLifecycleService>>());
    }

    public void Dispose() => _serviceProvider.Dispose();

    private async Task<string> SeedSystemAsync(string name = "Privacy Integration System")
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var system = new RegisteredSystem
        {
            Name = name,
            SystemType = SystemType.MajorApplication,
            MissionCriticality = MissionCriticality.MissionEssential,
            HostingEnvironment = "Azure Government",
            CreatedBy = "integration-test"
        };
        db.RegisteredSystems.Add(system);
        await db.SaveChangesAsync();
        return system.Id;
    }

    // ─── T055: End-to-end PTA → PIA → gate flow ────────────────────────

    [Fact]
    public async Task FullPrivacyFlow_PtaPiaGateCheck()
    {
        var systemId = await SeedSystemAsync();

        // Step 1: Create PTA with PII info types → PiaRequired
        var pta = await _privacyService.CreatePtaAsync(
            systemId,
            analyzedBy: "isso@test.com",
            manualMode: true,
            collectsPii: true,
            piiCategories: ["SSN", "DOB"]);

        pta.Determination.Should().Be(PtaDetermination.PiaRequired);

        // Step 2: Generate PIA
        var pia = await _privacyService.GeneratePiaAsync(systemId, "isso@test.com");
        pia.Status.Should().Be(PiaStatus.Draft);

        // Step 3: Gate check should fail (PIA not approved)
        var gates = await _lifecycleService.CheckGateConditionsAsync(systemId, RmfPhase.Categorize);
        var privacyGate = gates.FirstOrDefault(g => g.GateName == "Privacy Readiness");
        privacyGate.Should().NotBeNull();
        privacyGate!.Passed.Should().BeFalse();

        // Step 4: Approve PIA
        await _privacyService.ReviewPiaAsync(systemId, PiaReviewDecision.Approved, "No issues found", "issm@test.com");

        // Step 5: Gate should now pass
        gates = await _lifecycleService.CheckGateConditionsAsync(systemId, RmfPhase.Categorize);
        privacyGate = gates.FirstOrDefault(g => g.GateName == "Privacy Readiness");
        privacyGate!.Passed.Should().BeTrue();
    }

    // ─── T055: End-to-end Interconnection → ISA → agreement → gate flow ─

    [Fact]
    public async Task FullInterconnectionFlow_IsaAgreementGateCheck()
    {
        var systemId = await SeedSystemAsync("Interconnection Integration System");

        // Step 1: PTA (no PII) to satisfy Gate 3
        await _privacyService.CreatePtaAsync(systemId, analyzedBy: "isso@test.com");

        // Step 2: Add interconnection
        var ic = await _interconnectionService.AddInterconnectionAsync(
            systemId, "External LDAP", InterconnectionType.Vpn,
            DataFlowDirection.Bidirectional, "CUI", "isso@test.com",
            securityMeasures: ["TLS 1.3", "IP Filtering"]);

        ic.TargetSystemName.Should().Be("External LDAP");

        // Step 3: Gate 4 should fail (no agreement)
        var gates = await _lifecycleService.CheckGateConditionsAsync(systemId, RmfPhase.Categorize);
        var icGate = gates.FirstOrDefault(g => g.GateName == "Interconnection Documentation");
        icGate.Should().NotBeNull();
        icGate!.Passed.Should().BeFalse();

        // Step 4: Register and sign agreement
        var agreement = await _interconnectionService.RegisterAgreementAsync(
            ic.InterconnectionId, AgreementType.Isa, "ISA with External LDAP", "isso@test.com");
        await _interconnectionService.UpdateAgreementAsync(
            agreement.Id,
            status: AgreementStatus.Signed,
            effectiveDate: DateTime.UtcNow,
            expirationDate: DateTime.UtcNow.AddYears(1));

        // Step 5: Activate interconnection
        await _interconnectionService.UpdateInterconnectionAsync(
            ic.InterconnectionId, status: InterconnectionStatus.Active);

        // Step 6: Gate 4 should now pass
        gates = await _lifecycleService.CheckGateConditionsAsync(systemId, RmfPhase.Categorize);
        icGate = gates.FirstOrDefault(g => g.GateName == "Interconnection Documentation");
        icGate!.Passed.Should().BeTrue();
    }

    // ─── T062: Performance assertions ───────────────────────────────────

    [Fact]
    public async Task PtaCreation_CompletesWithin5Seconds()
    {
        var systemId = await SeedSystemAsync("Perf PTA System");
        var sw = Stopwatch.StartNew();
        await _privacyService.CreatePtaAsync(systemId, analyzedBy: "test");
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(5000);
    }

    [Fact]
    public async Task PiaGeneration_CompletesWithin30Seconds()
    {
        var systemId = await SeedSystemAsync("Perf PIA System");
        await _privacyService.CreatePtaAsync(systemId,
            analyzedBy: "test", manualMode: true, collectsPii: true,
            piiCategories: ["SSN"]);
        var sw = Stopwatch.StartNew();
        await _privacyService.GeneratePiaAsync(systemId, "test");
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(30000);
    }

    [Fact]
    public async Task GateEvaluation_CompletesWithin2Seconds()
    {
        var systemId = await SeedSystemAsync("Perf Gate System");
        var sw = Stopwatch.StartNew();
        await _lifecycleService.CheckGateConditionsAsync(systemId, RmfPhase.Categorize);
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(2000);
    }

    [Fact]
    public async Task AgreementValidation_CompletesWithin5Seconds()
    {
        var systemId = await SeedSystemAsync("Perf Validation System");
        var sw = Stopwatch.StartNew();
        await _interconnectionService.ValidateAgreementsAsync(systemId);
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(5000);
    }
}
