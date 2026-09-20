using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;

public class ScheduledTaskRunMapping : IEntityTypeConfiguration<ScheduledTaskRun>
{
    public void Configure(EntityTypeBuilder<ScheduledTaskRun> builder)
    {
        builder
            .HasKey(r => r.TaskKey)
            .HasName("pk_scheduled_task_runs");
    }
}
