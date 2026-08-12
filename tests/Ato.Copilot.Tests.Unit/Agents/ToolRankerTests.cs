using System.Collections.Generic;
using System.Threading.Tasks;
using Ato.Copilot.Agents.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ato.Copilot.Tests.Unit.Agents;

/// <summary>
/// Unit tests for the TF-IDF and embedding-based tool ranking infrastructure.
/// These tests are the regression gate for tool selection — if they turn red,
/// a phrasing-variant or always-include regression has been introduced.
/// </summary>
public class ToolRankerTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static FakeBaseTool MakeTool(string name, string description = "")
        => new(name, description);

    private static IReadOnlyList<BaseTool> MakeTools(params (string Name, string Desc)[] specs)
    {
        var list = new List<BaseTool>();
        foreach (var (n, d) in specs)
            list.Add(MakeTool(n, d));
        return list;
    }

    // ---------------------------------------------------------------------------
    // TfIdfToolRanker — tokeniser
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("hello world", new[] { "hello", "world" })]
    [InlineData("cac_status", new[] { "cac", "status" })]
    [InlineData("the quick brown fox", new[] { "quick", "brown", "fox" })] // 'the' is stopword
    [InlineData("", new string[0])]
    public void Tokenise_SplitsAndFiltersCorrectly(string input, string[] expected)
    {
        var result = TfIdfToolRanker.Tokenise(input);
        Assert.Equal(new HashSet<string>(expected), new HashSet<string>(result));
    }

    // ---------------------------------------------------------------------------
    // TfIdfToolRanker — always-include behaviour
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("compliance_assess")]
    [InlineData("assessment_run")]
    [InlineData("control_update")]
    [InlineData("audit_log")]
    [InlineData("rmf_categorize")]
    [InlineData("ssp_generate")]
    public async Task CorePrefixTools_AlwaysScore1_RegardlessOfMessage(string toolName)
    {
        var ranker = new TfIdfToolRanker();
        var tools = MakeTools((toolName, "A core compliance tool"));
        var ranked = await ranker.RankAsync("completely unrelated kanban board message", tools);

        Assert.Single(ranked);
        Assert.Equal(1.0, ranked[0].Score);
        Assert.Contains("always-include", ranked[0].Reason);
    }

    // ---------------------------------------------------------------------------
    // TfIdfToolRanker — relevance ordering
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RelevantTool_RanksHigherThanUnrelatedTool()
    {
        var ranker = new TfIdfToolRanker();
        var tools = MakeTools(
            ("kanban_create_task", "Create a task on the Kanban board"),
            ("cac_authenticate", "Authenticate using a CAC smart card"),
            ("pim_elevate", "Request privileged access via PIM")
        );

        // Message is clearly about task management
        var ranked = await ranker.RankAsync("I need to create a new task on the board", tools);

        // kanban_create_task should outscore cac_authenticate and pim_elevate
        var kanban = ranked[0];
        Assert.Equal("kanban_create_task", kanban.Tool.Name);
        Assert.True(kanban.Score > ranked[1].Score || ranked[1].Score == 0,
            "kanban tool should rank above unrelated tools");
    }

    [Fact]
    public async Task SynonymPhrase_StillSurfacesRelevantTool()
    {
        // This is the core regression test for the old keyword approach:
        // "smart card" should surface cac_authenticate even without the literal keyword "cac"
        var ranker = new TfIdfToolRanker();
        var tools = MakeTools(
            ("cac_authenticate", "Authenticate user with DoD Common Access Card smart card"),
            ("kanban_create_task", "Create a new task item on the project board"),
            ("pim_elevate", "Request just-in-time privileged access")
        );

        var ranked = await ranker.RankAsync("I need to authenticate with my smart card", tools);

        var cacRanked = ranked.First(r => r.Tool.Name == "cac_authenticate");
        var kanbanRanked = ranked.First(r => r.Tool.Name == "kanban_create_task");
        // cac_authenticate should beat kanban because "authenticate" and "smart" and "card"
        // are all in the cac_authenticate description
        Assert.True(cacRanked.Score >= kanbanRanked.Score,
            $"cac_authenticate score {cacRanked.Score:F4} should be >= kanban {kanbanRanked.Score:F4}");
    }

    // ---------------------------------------------------------------------------
    // TfIdfToolRanker — boundary: empty inputs
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task EmptyTools_ReturnsEmpty()
    {
        var ranker = new TfIdfToolRanker();
        var ranked = await ranker.RankAsync("any message", new List<BaseTool>());
        Assert.Empty(ranked);
    }

    [Fact]
    public async Task EmptyMessage_DoesNotThrow_CoreToolsStillIncluded()
    {
        var ranker = new TfIdfToolRanker();
        var tools = MakeTools(
            ("compliance_assess", "Run compliance assessment"),
            ("kanban_create_task", "Create task")
        );

        var ranked = await ranker.RankAsync(string.Empty, tools);
        Assert.Equal(2, ranked.Count);
        var core = ranked.First(r => r.Tool.Name == "compliance_assess");
        Assert.Equal(1.0, core.Score);
    }

    [Fact]
    public async Task LargeToolSet_ReturnsSameSizeOrdered()
    {
        var ranker = new TfIdfToolRanker();
        var specs = Enumerable.Range(0, 150)
            .Select(i => ($"tool_{i}", $"Description for tool number {i} with various words"))
            .ToList();
        var tools = MakeTools(specs.ToArray());

        var ranked = await ranker.RankAsync("find description with words", tools);

        Assert.Equal(150, ranked.Count);
        // Verify descending order
        for (var i = 1; i < ranked.Count; i++)
            Assert.True(ranked[i - 1].Score >= ranked[i].Score,
                $"Scores must be in descending order at index {i}");
    }


    // ---------------------------------------------------------------------------
    // TfIdfToolRanker — over-budget enforcement (> 128 tool budget)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// When the ranker returns more tools than the Azure OpenAI request limit (128),
    /// simulating BaseAgent.SelectToolsForMessageAsync: Take(128) must yield the
    /// highest-ranked tools and the remaining 22 are correctly identified as excluded.
    /// Regression gate for the audit-trail exclusion path.
    /// </summary>
    [Fact]
    public async Task OverBudget_First128Selected_Remainder22Excluded()
    {
        const int totalTools = 150;
        const int budget = 128;

        var ranker = new TfIdfToolRanker();
        var specs = Enumerable.Range(0, totalTools)
            .Select(i => ($"tool_{i:D3}", $"Description for tool {i:D3} covers topic area {i % 10}"))
            .ToArray();
        var tools = MakeTools(specs);

        var ranked = await ranker.RankAsync("topic area description", tools);

        // Ranker returns ALL tools; the caller (BaseAgent.SelectToolsForMessageAsync) applies the cap.
        Assert.Equal(totalTools, ranked.Count);

        var selected = ranked.Take(budget).ToList();
        var excluded = ranked.Skip(budget).ToList();

        Assert.Equal(budget, selected.Count);
        Assert.Equal(totalTools - budget, excluded.Count);

        // Every excluded tool must score <= the lowest-scoring selected tool.
        var minSelectedScore = selected.Min(r => r.Score);
        var maxExcludedScore = excluded.Max(r => r.Score);
        Assert.True(minSelectedScore >= maxExcludedScore,
            $"Excluded tools must rank below all selected tools. " +
            $"Min selected={minSelectedScore:F4}, max excluded={maxExcludedScore:F4}");
    }

    // ---------------------------------------------------------------------------
    // TfIdfToolRanker — tie scores
    // ---------------------------------------------------------------------------

    /// <summary>
    /// When multiple tools tie at score 1.0 (all core-prefix), the ranker
    /// must not throw and must return all tools with stable count.
    /// </summary>
    [Fact]
    public async Task TiedScores_DoNotThrow_AllToolsReturned()
    {
        var ranker = new TfIdfToolRanker();
        var tools = MakeTools(
            ("compliance_assess", "Run compliance assessment"),
            ("assessment_run", "Execute assessment run"),
            ("control_update", "Update control status"),
            ("audit_log", "Retrieve audit log")
        );

        var ranked = await ranker.RankAsync("some completely unrelated message xyz", tools);

        Assert.Equal(4, ranked.Count);
        Assert.All(ranked, r => Assert.Equal(1.0, r.Score));
    }

    // ---------------------------------------------------------------------------
    // TfIdfToolRanker — phrasing variant regression
    // (these forms previously dropped tools under the keyword-budget approach)
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("I want to log in using my government-issued card", "cac_authenticate")]
    [InlineData("authenticate with PIV", "cac_authenticate")]
    [InlineData("privileged access activation", "pim_elevate")]
    [InlineData("request temporary elevated privileges", "pim_elevate")]
    [InlineData("continuous monitoring alert rule", "watch_alert_create")]
    [InlineData("set up a monitoring notification", "watch_alert_create")]
    public async Task PhrasingVariants_SurfaceCorrectToolInTopTwo(string message, string expectedTool)
    {
        var ranker = new TfIdfToolRanker();
        var tools = MakeTools(
            ("cac_authenticate", "Authenticate user with DoD Common Access Card PIV smart card certificate"),
            ("pim_elevate", "Activate privileged access elevation request via PIM"),
            ("watch_alert_create", "Create continuous monitoring alert notification rule"),
            ("kanban_create_task", "Create a new task on the remediation project board"),
            ("system_register", "Register a new system for RMF ATO authorization")
        );

        var ranked = await ranker.RankAsync(message, tools);

        var topTwo = ranked.Take(2).Select(r => r.Tool.Name).ToList();
        Assert.Contains(expectedTool, (IEnumerable<string>)topTwo);
    }

    // ---------------------------------------------------------------------------
    // FallbackToolRanker
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FallbackRanker_UsesPrimaryWhenItReturnsResults()
    {
        var primary = new AlwaysReturnOneRanker("primary-result");
        var secondary = new AlwaysReturnOneRanker("secondary-result");
        var fallback = new FallbackToolRanker(primary, secondary,
            NullLogger<FallbackToolRanker>.Instance);

        var tools = MakeTools(("t1", "desc"));
        var result = await fallback.RankAsync("hello", tools);

        Assert.Single(result);
        Assert.Equal("primary-result", result[0].Reason);
    }

    [Fact]
    public async Task FallbackRanker_UsesFallbackWhenPrimaryReturnsEmpty()
    {
        var primary = new EmptyRanker();
        var secondary = new AlwaysReturnOneRanker("secondary-result");
        var fallback = new FallbackToolRanker(primary, secondary,
            NullLogger<FallbackToolRanker>.Instance);

        var tools = MakeTools(("t1", "desc"));
        var result = await fallback.RankAsync("hello", tools);

        Assert.Single(result);
        Assert.Equal("secondary-result", result[0].Reason);
    }

    // ---------------------------------------------------------------------------
    // Inner stubs
    // ---------------------------------------------------------------------------

    private sealed class FakeBaseTool : BaseTool
    {
        private readonly string _name;
        private readonly string _desc;
        public FakeBaseTool(string name, string description)
            : base(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance) { _name = name; _desc = description; }
        public override string Name => _name;
        public override string Description => _desc;
        public override System.Collections.Generic.IReadOnlyDictionary<string, ToolParameter> Parameters
            => new System.Collections.Generic.Dictionary<string, ToolParameter>();
        public override Task<string> ExecuteCoreAsync(System.Collections.Generic.Dictionary<string, object?> arguments,
            System.Threading.CancellationToken cancellationToken = default)
            => Task.FromResult("ok");
    }

    private sealed class AlwaysReturnOneRanker : IToolRanker
    {
        private readonly string _reason;
        public AlwaysReturnOneRanker(string reason) => _reason = reason;
        public Task<IReadOnlyList<RankedTool>> RankAsync(
            string message, IReadOnlyList<BaseTool> tools,
            System.Threading.CancellationToken cancellationToken = default)
        {
            IReadOnlyList<RankedTool> result = tools.Count == 0
                ? Array.Empty<RankedTool>()
                : new[] { new RankedTool(tools[0], 1.0, _reason) };
            return Task.FromResult(result);
        }
    }

    private sealed class EmptyRanker : IToolRanker
    {
        public Task<IReadOnlyList<RankedTool>> RankAsync(
            string message, IReadOnlyList<BaseTool> tools,
            System.Threading.CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RankedTool>>(Array.Empty<RankedTool>());
    }
}
