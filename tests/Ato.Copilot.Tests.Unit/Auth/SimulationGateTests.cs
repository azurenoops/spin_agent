using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Configuration.Auth;
using Ato.Copilot.Mcp.Endpoints.Auth;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Auth;

/// <summary>
/// Feature 051 T125 [US7] — unit coverage for the simulation-panel gate.
/// </summary>
/// <remarks>
/// <para>The gate is the first of three layers of the simulation-panel
/// security invariant (research.md § R-Summary item 4):
/// (1) <c>/login-config</c> omits the descriptor, (2) SPA route guard
/// refuses to mount, (3) <c>POST /api/auth/simulate</c> returns a bare 404
/// in non-Development.</para>
/// <para>These unit tests exercise the (1)+(3) branches that are awkward to
/// express in integration tests because flipping <c>ASPNETCORE_ENVIRONMENT</c>
/// mid-process destabilises the <c>WebApplicationFactory</c> listener
/// pipeline (the env var is process-global and the bootstrap Serilog config
/// reads it directly). The wire-level checks for the non-Development case
/// live in <c>tests/Ato.Copilot.Tests.Integration/Auth/</c>.</para>
/// </remarks>
public class SimulationGateTests
{
    [Fact]
    public void DevelopmentIdentities_IncludeWorkspacePersonas_WithoutImplicitRoleGrants()
    {
        // Arrange
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Ato.Copilot.sln")))
            root = root.Parent;
        root.Should().NotBeNull("the test runs from the repository build output");
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(root!.FullName, "src", "Ato.Copilot.Mcp", "appsettings.Development.json"))
            .Build();

        // Act
        var options = config.GetSection("CacAuth").Get<CacAuthOptions>()!;

        // Assert
        options.SimulatedIdentities.Select(i => i.IdentityId).Should().OnlyHaveUniqueItems();
        options.SimulatedIdentities.Select(i => i.Oid).Should().OnlyHaveUniqueItems();
        foreach (var persona in new[] { "OrganizationAdmin", "MissionOwner", "SystemOwner", "ISSM", "SCA", "AuthorizingOfficial" })
        {
            var identity = options.SimulatedIdentities.Should().ContainSingle(i => i.Persona == persona).Which;
            identity.Roles.Should().BeEmpty("workspace roles require explicit assignments, not persona labels");
            Guid.TryParse(identity.Oid, out _).Should().BeTrue();
            Guid.TryParse(identity.Tid, out _).Should().BeTrue();
        }
        options.SimulatedIdentities.Should().Contain(i => i.IdentityId == "dev-cspadmin");
        options.SimulatedIdentities.Should().Contain(i => i.IdentityId == "dev-isso");
        options.SimulatedIdentities.Should().Contain(i => i.IdentityId == "dev-soc-analyst");
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = string.Empty;
        public string ApplicationName { get; set; } = "Ato.Copilot.Mcp";
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private static FakeHostEnvironment Env(string name) => new() { EnvironmentName = name };

    private static SimulatedIdentityDescriptor SampleIdentity(string id = "dev-id-a") => new()
    {
        IdentityId = id,
        DisplayName = "Dev A",
        Oid = "10000000-0000-0000-0000-000000000aaa",
        Tid = "00000000-0000-0000-0000-000000000001",
        TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
        Persona = "Tester",
    };

    // ─── ShouldSurfaceDescriptor — three-condition gate matrix ──────────

    [Fact]
    public void Gate_Closed_When_EnvIsNotDevelopment_EvenIfModeTrue_AndIdentities()
    {
        // Arrange
        var opts = new CacAuthOptions
        {
            SimulationMode = true,
            SimulatedIdentities = new() { SampleIdentity() },
        };

        // Act + Assert
        SimulationGate.ShouldSurfaceDescriptor(Env("Staging"), opts).Should().BeFalse();
        SimulationGate.ShouldSurfaceDescriptor(Env("Production"), opts).Should().BeFalse();
        SimulationGate.ShouldSurfaceDescriptor(Env("Testing"), opts).Should().BeFalse();
    }

    [Fact]
    public void Gate_Closed_When_SimulationModeFalse_EvenIfDevAndIdentities()
    {
        var opts = new CacAuthOptions
        {
            SimulationMode = false,
            SimulatedIdentities = new() { SampleIdentity() },
        };

        SimulationGate.ShouldSurfaceDescriptor(Env("Development"), opts).Should().BeFalse();
    }

    [Fact]
    public void Gate_Closed_When_IdentitiesEmpty_EvenIfDevAndModeTrue()
    {
        var opts = new CacAuthOptions
        {
            SimulationMode = true,
            SimulatedIdentities = new(),
        };

        SimulationGate.ShouldSurfaceDescriptor(Env("Development"), opts).Should().BeFalse();
    }

    [Fact]
    public void Gate_Closed_When_OnlyInvalidIdentities()
    {
        // An identity with a blank IdentityId can never be selected by
        // POST /api/auth/simulate, so the gate MUST treat the list as empty.
        var opts = new CacAuthOptions
        {
            SimulationMode = true,
            SimulatedIdentities = new()
            {
                new SimulatedIdentityDescriptor { IdentityId = "  " },
            },
        };

        SimulationGate.ShouldSurfaceDescriptor(Env("Development"), opts).Should().BeFalse();
    }

    [Fact]
    public void Gate_Open_When_AllThreeConditionsHold()
    {
        var opts = new CacAuthOptions
        {
            SimulationMode = true,
            SimulatedIdentities = new() { SampleIdentity() },
        };

        SimulationGate.ShouldSurfaceDescriptor(Env("Development"), opts).Should().BeTrue();
    }

    [Fact]
    public void Gate_Closed_When_CacAuthIsNull()
    {
        SimulationGate.ShouldSurfaceDescriptor(Env("Development"), cacAuth: null!).Should().BeFalse();
    }

    // ─── BuildDescriptor wire shape ─────────────────────────────────────

    [Fact]
    public void BuildDescriptor_ReturnsNull_WhenGateClosed()
    {
        var opts = new CacAuthOptions { SimulationMode = false };
        SimulationGate.BuildDescriptor(Env("Development"), opts).Should().BeNull();
    }

    [Fact]
    public void BuildDescriptor_ReturnsArray_OfWireShape_WhenGateOpen()
    {
        // Arrange
        var opts = new CacAuthOptions
        {
            SimulationMode = true,
            SimulatedIdentities = new()
            {
                SampleIdentity("dev-cspadmin"),
                SampleIdentity("dev-isso"),
            },
        };

        // Act
        var d = SimulationGate.BuildDescriptor(Env("Development"), opts);

        // Assert — shape mirrors `contracts/frontend-types.md § 1`.
        d.Should().NotBeNull();
        var json = System.Text.Json.JsonSerializer.SerializeToElement(d);
        var identities = json.GetProperty("identities");
        identities.GetArrayLength().Should().Be(2);
        var first = identities[0];
        first.GetProperty("id").GetString().Should().Be("dev-cspadmin");
        first.GetProperty("displayName").GetString().Should().Be("Dev A");
        first.GetProperty("persona").GetString().Should().Be("Tester");
        first.GetProperty("tenantId").GetString().Should().Be("00000000-0000-0000-0000-000000000001");
        first.GetProperty("roles").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Array);
    }

    [Fact]
    public void BuildDescriptor_SkipsEntriesWithBlankIdentityId()
    {
        var opts = new CacAuthOptions
        {
            SimulationMode = true,
            SimulatedIdentities = new()
            {
                SampleIdentity("dev-good"),
                new SimulatedIdentityDescriptor { IdentityId = " " },
            },
        };

        var d = SimulationGate.BuildDescriptor(Env("Development"), opts);
        var json = System.Text.Json.JsonSerializer.SerializeToElement(d);
        json.GetProperty("identities").GetArrayLength().Should().Be(1);
    }

    // ─── FindIdentity lookup ────────────────────────────────────────────

    [Fact]
    public void FindIdentity_ReturnsNull_ForUnknownId()
    {
        var opts = new CacAuthOptions
        {
            SimulatedIdentities = new() { SampleIdentity("dev-known") },
        };
        SimulationGate.FindIdentity(opts, "not-there").Should().BeNull();
    }

    [Fact]
    public void FindIdentity_ReturnsNull_ForNullOrBlankId()
    {
        var opts = new CacAuthOptions
        {
            SimulatedIdentities = new() { SampleIdentity("dev-known") },
        };
        SimulationGate.FindIdentity(opts, null).Should().BeNull();
        SimulationGate.FindIdentity(opts, "  ").Should().BeNull();
    }

    [Fact]
    public void FindIdentity_ReturnsMatch_WhenIdsAlign()
    {
        var opts = new CacAuthOptions
        {
            SimulatedIdentities = new() { SampleIdentity("dev-known") },
        };
        var hit = SimulationGate.FindIdentity(opts, "dev-known");
        hit.Should().NotBeNull();
        hit!.IdentityId.Should().Be("dev-known");
    }

    // ─── T124 — SimulationBlocked log scope tag ─────────────────────────

    [Fact]
    public void LogSimulationBlocked_EmitsWarning_WithSecurityScopeTag()
    {
        // Arrange
        var capture = new ScopeCapturingLogger();

        // Act
        SimulationGate.LogSimulationBlocked(capture, environmentName: "Staging", attemptedIdentityId: "dev-cspadmin");

        // Assert — exactly one log entry, Warning level, message text matches
        capture.Entries.Should().HaveCount(1);
        var entry = capture.Entries[0];
        entry.Level.Should().Be(Microsoft.Extensions.Logging.LogLevel.Warning,
            "FR-024 — the blocked event MUST be Warning-or-higher so default SIEM filters surface it");
        entry.Message.Should().Contain("Simulation blocked");
        entry.Message.Should().Contain("Staging");

        // Assert — severity=Security scope tag present (T124 / SIEM elevation invariant)
        entry.Scopes.Should().Contain(
            s => s.ContainsKey("severity")
                 && Equals(s["severity"], "Security"),
            "T124 — the SimulationBlocked log line MUST include a `severity=Security` scope tag for SIEM elevation");
        entry.Scopes.Should().Contain(
            s => s.ContainsKey("attemptedIdentityId") && Equals(s["attemptedIdentityId"], "dev-cspadmin"));
        entry.Scopes.Should().Contain(
            s => s.ContainsKey("environment") && Equals(s["environment"], "Staging"));
    }

    [Fact]
    public void LogSimulationBlocked_NormalizesNullIdentityId_ToEmptyString()
    {
        var capture = new ScopeCapturingLogger();
        SimulationGate.LogSimulationBlocked(capture, "Production", attemptedIdentityId: null);

        capture.Entries.Should().HaveCount(1);
        capture.Entries[0].Scopes.Should().Contain(
            s => s.ContainsKey("attemptedIdentityId") && Equals(s["attemptedIdentityId"], string.Empty));
    }

    // ─── Test logger that captures scopes ───────────────────────────────

    private sealed class ScopeCapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        private readonly List<IReadOnlyDictionary<string, object?>> _activeScopes = new();
        public List<Entry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            IReadOnlyDictionary<string, object?> dict = state switch
            {
                IReadOnlyDictionary<string, object?> ro => ro,
                IDictionary<string, object?> md => new Dictionary<string, object?>(md),
                IEnumerable<KeyValuePair<string, object?>> kvps => kvps.ToDictionary(k => k.Key, k => k.Value),
                _ => new Dictionary<string, object?> { ["state"] = state },
            };
            _activeScopes.Add(dict);
            return new ScopePopper(_activeScopes);
        }

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new Entry(
                logLevel,
                formatter(state, exception),
                _activeScopes.ToArray()));
        }

        public sealed record Entry(
            Microsoft.Extensions.Logging.LogLevel Level,
            string Message,
            IReadOnlyList<IReadOnlyDictionary<string, object?>> Scopes);

        private sealed class ScopePopper : IDisposable
        {
            private readonly List<IReadOnlyDictionary<string, object?>> _list;
            public ScopePopper(List<IReadOnlyDictionary<string, object?>> list) => _list = list;
            public void Dispose()
            {
                if (_list.Count > 0) _list.RemoveAt(_list.Count - 1);
            }
        }
    }
}
