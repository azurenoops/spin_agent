using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Ato.Copilot.Agents.Common;

/// <summary>
/// Offline-capable tool ranker using TF-IDF cosine similarity over tool names and
/// descriptions.  Does not require a network call and is the fallback when an
/// embedding provider is unavailable.
///
/// Addresses the brittleness of the keyword-table approach: where "authenticate
/// with smart card" previously received no CAC tools (no match against the
/// literal keyword list), TF-IDF scores by shared vocabulary so synonym phrases
/// still surface the right tools.
///
/// Always-include prefixes are scored 1.0 and cannot be ranked out.
/// </summary>
public sealed class TfIdfToolRanker : IToolRanker
{
    // Prefixes whose tools are always scored 1.0 regardless of message content.
    // Kept in sync with BaseAgent.AlwaysIncludePrefixes via the same list shape.
    internal static readonly string[] CorePrefixes = new[]
    {
        "compliance_", "assessment_", "control_", "document_", "evidence_",
        "remediation_", "audit_", "nist_", "rmf_", "conmon_", "emass_",
        "system_", "poam_", "ssp_", "ato_", "authorization_", "categorize_",
        "role_", "narrative_", "boundary_",
    };

    // Category prefixes whose tools are only relevant when the message indicates need.
    internal static readonly string[] CategoryPrefixes = new[]
    {
        "kanban_", "cac_", "pim_", "jit_", "watch_",
    };

    // Stopwords to suppress from IDF computation.
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "is", "it", "in", "of", "to", "and", "or", "for",
        "with", "on", "at", "by", "from", "as", "this", "that", "be", "are",
        "was", "were", "can", "will", "do", "does", "did", "has", "have",
        "had", "not", "but", "if", "then", "so", "its", "my", "your", "our"
    };

    // IDF table: term → log((N+1)/(df+1)) + 1 (smoothed)
    private readonly ConcurrentDictionary<string, double> _idf = new(StringComparer.OrdinalIgnoreCase);

    // Tool corpus TF vectors (precomputed at first ranking call)
    private IReadOnlyList<BaseTool>? _lastTools;
    private (BaseTool Tool, Dictionary<string, double> Tf)[]? _corpus;
    private readonly object _corpusLock = new();

    public Task<IReadOnlyList<RankedTool>> RankAsync(
        string message,
        IReadOnlyList<BaseTool> tools,
        CancellationToken cancellationToken = default)
    {
        if (tools.Count == 0)
            return Task.FromResult<IReadOnlyList<RankedTool>>(Array.Empty<RankedTool>());

        EnsureCorpus(tools);

        var messageTokens = Tokenise(message);
        var messageTf = ComputeTf(messageTokens);

        var results = new RankedTool[tools.Count];
        var corpus = _corpus!;

        for (var i = 0; i < corpus.Length; i++)
        {
            var (tool, toolTf) = corpus[i];
            var name = tool.Name;

            // Core tools always score 1.0
            if (IsCore(name))
            {
                results[i] = new RankedTool(tool, 1.0, "always-include:core-prefix");
                continue;
            }

            // Category tools: compute TF-IDF cosine similarity
            var score = CosineSimilarity(messageTf, toolTf);
            var reason = score > 0
                ? $"tfidf:score={score:F4}"
                : "tfidf:no-match";

            results[i] = new RankedTool(tool, score, reason);
        }

        // Sort descending by score
        Array.Sort(results, (a, b) => b.Score.CompareTo(a.Score));

        return Task.FromResult<IReadOnlyList<RankedTool>>(results);
    }

    // --- Internal helpers ---

    private static bool IsCore(string toolName)
        => Array.Exists(CorePrefixes, p =>
            toolName.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private void EnsureCorpus(IReadOnlyList<BaseTool> tools)
    {
        // Rebuild only when the tool set changes (reference equality on list is fine
        // because IEnumerable<BaseTool> from DI returns a stable collection).
        if (ReferenceEquals(_lastTools, tools))
            return;

        lock (_corpusLock)
        {
            if (ReferenceEquals(_lastTools, tools))
                return;

            BuildCorpus(tools);
            _lastTools = tools;
        }
    }

    private void BuildCorpus(IReadOnlyList<BaseTool> tools)
    {
        var n = tools.Count;
        var corpus = new (BaseTool, Dictionary<string, double>)[n];
        var df = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // First pass: tokenise each tool document and collect DF
        for (var i = 0; i < n; i++)
        {
            var tool = tools[i];
            var doc = $"{tool.Name} {tool.Description ?? ""}";
            var tokens = Tokenise(doc);
            var tf = ComputeTf(tokens);
            corpus[i] = (tool, tf);

            foreach (var term in tf.Keys)
            {
                df[term] = df.GetValueOrDefault(term) + 1;
            }
        }

        // Second pass: compute IDF (smoothed)
        _idf.Clear();
        foreach (var (term, freq) in df)
        {
            _idf[term] = Math.Log((n + 1.0) / (freq + 1.0)) + 1.0;
        }

        _corpus = corpus;
    }

    private double CosineSimilarity(
        Dictionary<string, double> queryTf,
        Dictionary<string, double> docTf)
    {
        double dot = 0, qNorm = 0, dNorm = 0;

        foreach (var (term, qtf) in queryTf)
        {
            var idf = _idf.GetValueOrDefault(term, 1.0);
            var qTfIdf = qtf * idf;
            qNorm += qTfIdf * qTfIdf;

            if (docTf.TryGetValue(term, out var dtf))
            {
                var dTfIdf = dtf * idf;
                dot += qTfIdf * dTfIdf;
            }
        }

        foreach (var (term, dtf) in docTf)
        {
            var idf = _idf.GetValueOrDefault(term, 1.0);
            var dTfIdf = dtf * idf;
            dNorm += dTfIdf * dTfIdf;
        }

        if (qNorm == 0 || dNorm == 0) return 0;
        return dot / (Math.Sqrt(qNorm) * Math.Sqrt(dNorm));
    }

    /// <summary>
    /// Splits text into lowercase alphabetical tokens, removing stopwords.
    /// Also splits underscore-joined identifiers (e.g. "cac_status" → ["cac", "status"]).
    /// </summary>
    internal static List<string> Tokenise(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        var tokens = new List<string>();
        // Replace underscores and hyphens with spaces, then split on non-alpha
        var normalised = Regex.Replace(text, @"[_\-]", " ");
        foreach (var raw in Regex.Split(normalised, @"[^a-zA-Z]+"))
        {
            if (raw.Length < 2) continue;
            var tok = raw.ToLowerInvariant();
            if (!Stopwords.Contains(tok))
                tokens.Add(tok);
        }
        return tokens;
    }

    private static Dictionary<string, double> ComputeTf(List<string> tokens)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tokens)
            counts[t] = counts.GetValueOrDefault(t) + 1;

        var n = tokens.Count == 0 ? 1 : tokens.Count;
        var tf = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (term, count) in counts)
            tf[term] = (double)count / n;

        return tf;
    }
}
