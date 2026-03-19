using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services;

/// <summary>
/// One-time startup migration: converts legacy AuthorizationBoundary rows into
/// SystemComponent + BoundaryComponentAssignment records.
/// Idempotent via sentinel flag in __MigrationFlags table.
/// </summary>
public class BoundaryMigrationService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BoundaryMigrationService> _logger;
    private const string MigrationName = "F040_BoundaryToComponent";

    public BoundaryMigrationService(
        IServiceScopeFactory scopeFactory,
        ILogger<BoundaryMigrationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Ensure __MigrationFlags table exists
        await db.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID('__MigrationFlags', 'U') IS NULL CREATE TABLE __MigrationFlags (Name NVARCHAR(200) PRIMARY KEY, AppliedAt NVARCHAR(50) NOT NULL)",
            cancellationToken);

        // Idempotency check
        var alreadyRun = await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS \"Value\" FROM __MigrationFlags WHERE Name = {0}", MigrationName)
            .FirstOrDefaultAsync(cancellationToken);

        if (alreadyRun > 0)
        {
            _logger.LogInformation("Migration {MigrationName} already applied — skipping", MigrationName);
            return;
        }

        _logger.LogInformation("Starting migration {MigrationName}", MigrationName);

#pragma warning disable CS0618 // AuthorizationBoundary is [Obsolete]
        var boundaryRows = await db.AuthorizationBoundaries
            .AsNoTracking()
            .ToListAsync(cancellationToken);
#pragma warning restore CS0618

        if (boundaryRows.Count == 0)
        {
            _logger.LogInformation("No AuthorizationBoundary rows to migrate — setting flag");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO __MigrationFlags (Name, AppliedAt) VALUES ({0}, {1})",
                MigrationName, DateTime.UtcNow.ToString("O"));
            return;
        }

        _logger.LogInformation("Found {RowCount} AuthorizationBoundary rows to migrate", boundaryRows.Count);

        // Group by ResourceId for dedup
        var groups = boundaryRows.GroupBy(r => r.ResourceId).ToList();
        _logger.LogInformation("Dedup: {UniqueResources} unique resources from {TotalRows} rows",
            groups.Count, boundaryRows.Count);

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var componentMap = new Dictionary<string, string>(); // ResourceId → ComponentId
                var newComponents = new List<SystemComponent>();
                var newAssignments = new List<BoundaryComponentAssignment>();

                foreach (var group in groups)
                {
                    var representative = group.First();

                    // Parse resource group from ResourceId (Azure ARM format)
                    string? resourceGroup = null;
                    var parts = representative.ResourceId.Split('/');
                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        if (parts[i].Equals("resourceGroups", StringComparison.OrdinalIgnoreCase) ||
                            parts[i].Equals("resourcegroups", StringComparison.OrdinalIgnoreCase))
                        {
                            resourceGroup = parts[i + 1];
                            break;
                        }
                    }

                    var component = new SystemComponent
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = representative.ResourceName ?? $"Migrated: {representative.ResourceId.Split('/').LastOrDefault() ?? representative.ResourceId}",
                        ComponentType = ComponentType.Thing,
                        RegisteredSystemId = null, // Org-wide
                        AzureResourceId = representative.ResourceId,
                        AzureResourceType = representative.ResourceType,
                        AzureResourceGroup = resourceGroup,
                        Status = ComponentStatus.Active,
                        CreatedAt = DateTime.UtcNow,
                    };
                    newComponents.Add(component);
                    componentMap[representative.ResourceId] = component.Id;
                }

                _logger.LogInformation("Creating {ComponentCount} org-wide SystemComponent records", newComponents.Count);
                db.SystemComponents.AddRange(newComponents);

                // Create assignments for each original row
                foreach (var row in boundaryRows)
                {
                    if (!componentMap.TryGetValue(row.ResourceId, out var componentId)) continue;

                    // Determine the boundary definition to assign to
                    var boundaryDefinitionId = row.AuthorizationBoundaryDefinitionId;
                    if (string.IsNullOrEmpty(boundaryDefinitionId))
                    {
                        // Find the default boundary definition for this system
                        boundaryDefinitionId = await db.AuthorizationBoundaryDefinitions
                            .Where(d => d.RegisteredSystemId == row.RegisteredSystemId)
                            .Select(d => d.Id)
                            .FirstOrDefaultAsync(cancellationToken);
                    }

                    if (string.IsNullOrEmpty(boundaryDefinitionId))
                    {
                        _logger.LogWarning("No boundary definition found for system {SystemId}, skipping assignment for resource {ResourceId}",
                            row.RegisteredSystemId, row.ResourceId);
                        continue;
                    }

                    newAssignments.Add(new BoundaryComponentAssignment
                    {
                        Id = Guid.NewGuid().ToString(),
                        SystemComponentId = componentId,
                        AuthorizationBoundaryDefinitionId = boundaryDefinitionId,
                        IsInScope = row.IsInBoundary,
                        ExclusionRationale = row.ExclusionRationale,
                        InheritanceProvider = row.InheritanceProvider,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = row.AddedBy,
                    });
                }

                _logger.LogInformation("Creating {AssignmentCount} BoundaryComponentAssignment records", newAssignments.Count);
                db.BoundaryComponentAssignments.AddRange(newAssignments);

                await db.SaveChangesAsync(cancellationToken);

                // Insert sentinel flag
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO __MigrationFlags (Name, AppliedAt) VALUES ({0}, {1})",
                    MigrationName, DateTime.UtcNow.ToString("O"));

                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation("Migration {MigrationName} completed: {Components} components, {Assignments} assignments",
                    MigrationName, newComponents.Count, newAssignments.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Migration {MigrationName} failed — rolling back", MigrationName);
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
