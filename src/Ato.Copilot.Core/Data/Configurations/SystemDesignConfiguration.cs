using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Configurations;

public static class SystemDesignConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<SystemDesignWorkspace>().ToTable("SystemDesignWorkspaces")
            .HasIndex(x => new { x.TenantId, x.SystemId }).IsUnique();
        model.Entity<SystemDesignRevision>().ToTable("SystemDesignRevisions")
            .HasIndex(x => new { x.TenantId, x.SystemId, x.Revision }).IsUnique();
        model.Entity<SystemDesignLayoutRecord>().ToTable("SystemDesignLayouts")
            .HasIndex(x => new { x.TenantId, x.SystemId, x.View }).IsUnique();
    }
}
