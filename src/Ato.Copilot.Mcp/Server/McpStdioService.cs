using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Mcp.Server;

/// <summary>
/// Background service that runs the MCP server in stdio mode for CLI/agent consumption
/// </summary>
public class McpStdioService : BackgroundService
{
    private readonly McpServer _mcpServer;
    private readonly ILogger<McpStdioService> _logger;

    public McpStdioService(McpServer mcpServer, ILogger<McpStdioService> logger)
    {
        _mcpServer = mcpServer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Security Posture Intelligence Navigator in stdio mode");

        try
        {
            await _mcpServer.StartAsync();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("MCP stdio service shutting down");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in MCP stdio service");
            throw;
        }
    }
}
