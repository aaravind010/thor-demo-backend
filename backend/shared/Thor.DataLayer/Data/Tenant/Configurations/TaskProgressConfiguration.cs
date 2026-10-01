using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Data.TenantConfigurations;

public sealed class TaskProgressConfiguration : IEntityTypeConfiguration<TaskProgress>
{
    public void Configure(EntityTypeBuilder<TaskProgress> builder)
    {
        builder.Property(p => p.Attributes).HasColumnType("jsonb");

        // Supports fetching a task's progress history (most recent first).
        builder.HasIndex(p => new { p.TaskId, p.CreatedAt });
    }
}
