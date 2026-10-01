using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Data.TenantConfigurations;

public sealed class ScanTaskConfiguration : IEntityTypeConfiguration<ScanTask>
{
    public void Configure(EntityTypeBuilder<ScanTask> builder)
    {
        // Supports ScanTaskRepository.ClaimPendingTasksAsync's "oldest pending tasks" query.
        // Partial (Postgres-only): only Pending rows are indexed, so the index stays small and
        // fast regardless of how much Completed/Failed history accumulates in this table.
        builder.HasIndex(t => new { t.CreatedAt, t.Id })
            .HasFilter("status = 'Pending'");

        // Supports the stall-reclaim sweep in the same method ("InProgress tasks whose
        // LastHeartbeatAt is older than the stall timeout"). Partial for the same reason as
        // above, scoped to InProgress rows instead of Pending ones.
        builder.HasIndex(t => t.LastHeartbeatAt)
            .HasFilter("status = 'InProgress'");
    }
}
