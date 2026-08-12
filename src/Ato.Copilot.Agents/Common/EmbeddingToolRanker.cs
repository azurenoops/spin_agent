using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Common;

/// <summary>
/// Embedding-based tool ranker using Azure OpenAI (or any
/// <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> provider).
///
/// At first use, pre-computes embeddings for all tool descriptions and caches
/// them in memory.  On each message, embeds the message and ranks tools by
/// cosine similarity.  Core tools (always-include prefixes) retain a score
/// floor of 1.0 so they are never ranked out.
///
/// Falls back gracefully: if the embedding call fails, returns null so the
/// caller can switch to <see cref="TfIdfToolRanker"/>.
/// </summary>
public sealed class EmbeddingToolRanker : IToolRanker
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embedder;
    private readonly ILogger<EmbeddingToolRanker> _logger;

    // Cache: toolName → unit-normalised embedding vector
    private readonly Dictionary<string, float[]> _toolEmbeddings = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<BaseTool>? _indexedTools;
    private readonly SemaphoreSlim _indexLock = new(1, 1);

    public EmbeddingToolRanker(
        IEmbeddingGenerator<string, Embedding<float>> embedder,
        ILogger<EmbeddingToolRanker> logger)
    {
        _embedder = embedder;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RankedTool>> RankAsync(
        string message,
        IReadOnlyList<BaseTool> tools,
        CancellationToken cancellationToken = default)
    {
        if (tools.Count == 0)
            return Array.Empty<RankedTool>();

        await EnsureIndexAsync(tools, cancellationToken);

        // Embed the user message
        Embedding<float> messageEmbedding;
        try
        {
            var result = await _embedder.GenerateAsync(new[] { message }, cancellationToken: cancellationToken);
            messageEmbedding = result[0];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "EmbeddingToolRanker: failed to embed user message, ranking unavailable");
            return Array.Empty<RankedTool>(); // Caller falls back to TF-IDF
        }

        var msgVec = Normalise(messageEmbedding.Vector.ToArray());

        var results = new List<RankedTool>(tools.Count);
        foreach (var tool in tools)
        {
            // Core tools always score 1.0
            if (TfIdfToolRanker.CorePrefixes.Any(p =>
                    tool.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(new RankedTool(tool, 1.0, "always-include:core-prefix"));
                continue;
            }

            if (!_toolEmbeddings.TryGetValue(tool.Name, out var toolVec))
            {
                results.Add(new RankedTool(tool, 0.0, "embedding:not-indexed"));
                continue;
            }

            var score = CosineSimilarity(msgVec, toolVec);
            results.Add(new RankedTool(tool, score, $"embedding:score={score:F4}"));
        }

        results.Sort((a, b) => b.Score.CompareTo(a.Score));
        return results;
    }

    private async Task EnsureIndexAsync(IReadOnlyList<BaseTool> tools, CancellationToken cancellationToken)
    {
        if (ReferenceEquals(_indexedTools, tools))
            return;

        await _indexLock.WaitAsync(cancellationToken);
        try
        {
            if (ReferenceEquals(_indexedTools, tools))
                return;

            _logger.LogInformation(
                "EmbeddingToolRanker: indexing {Count} tools...", tools.Count);

            var nonCore = tools
                .Where(t => !TfIdfToolRanker.CorePrefixes.Any(p =>
                    t.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (nonCore.Count > 0)
            {
                var docs = nonCore.Select(t => $"{t.Name}: {t.Description ?? t.Name}").ToList();
                var embeddings = await _embedder.GenerateAsync(docs, cancellationToken: cancellationToken);

                _toolEmbeddings.Clear();
                for (var i = 0; i < nonCore.Count; i++)
                    _toolEmbeddings[nonCore[i].Name] = Normalise(embeddings[i].Vector.ToArray());
            }

            _indexedTools = tools;
            _logger.LogInformation(
                "EmbeddingToolRanker: indexed {Count} non-core tools", nonCore.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "EmbeddingToolRanker: indexing failed — embedding ranking unavailable until next call");
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private static float[] Normalise(float[] v)
    {
        var norm = (float)Math.Sqrt(v.Sum(x => (double)x * x));
        if (norm < 1e-9f) return v;
        return v.Select(x => x / norm).ToArray();
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;
        double dot = 0;
        for (var i = 0; i < a.Length; i++)
            dot += a[i] * b[i];
        // Vectors are pre-normalised so no need to divide by norms
        return Math.Clamp(dot, 0, 1);
    }
}
