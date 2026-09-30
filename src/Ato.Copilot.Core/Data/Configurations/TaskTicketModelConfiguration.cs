using Ato.Copilot.Core.Models.Poam;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Data.Configurations;

public static class TaskTicketModelConfiguration
{
    public static void ConfigureTaskTicketing(this ModelBuilder builder)
    {
        builder.Entity<TaskTicketLink>(entity =>
        {
            entity.ToTable("TaskTicketLinks");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.RegisteredSystemId, x.TaskId }).IsUnique();
            entity.Property(x => x.Provider).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.RowVersion).IsConcurrencyToken();
        });
        builder.Entity<TaskTicketAudit>(entity =>
        {
            entity.ToTable("TaskTicketAudits");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.TaskTicketLinkId });
        });
    }
}
