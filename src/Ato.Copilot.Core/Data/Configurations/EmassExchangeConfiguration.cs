using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ato.Copilot.Core.Data.Configurations;

public sealed class EmassExchangeConfiguration : IEntityTypeConfiguration<EmassExchangeRecord>
{
    public void Configure(EntityTypeBuilder<EmassExchangeRecord> entity)
    {
        entity.ToTable("EmassExchangeRecords");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasMaxLength(36);
        entity.Property(x => x.RegisteredSystemId).HasMaxLength(36).IsRequired();
        entity.Property(x => x.PackageId).HasMaxLength(36).IsRequired();
        entity.Property(x => x.PackageHash).HasMaxLength(128).IsRequired();
        entity.Property(x => x.Outcome).HasMaxLength(32).IsRequired();
        entity.Property(x => x.ReceivingWorkflow).HasMaxLength(200).IsRequired();
        entity.Property(x => x.ExternalReference).HasMaxLength(500).IsRequired();
        entity.Property(x => x.RecordedBy).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Notes).HasMaxLength(4000).IsRequired();
        entity.Property(x => x.SupersedesId).HasMaxLength(36);
        entity.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
        entity.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        entity.HasIndex(x => new { x.TenantId, x.RegisteredSystemId, x.Version }).IsUnique();
        entity.HasIndex(x => new { x.TenantId, x.RegisteredSystemId, x.IdempotencyKey }).IsUnique();
    }
}
