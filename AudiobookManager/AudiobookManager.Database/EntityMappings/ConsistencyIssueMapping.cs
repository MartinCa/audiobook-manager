using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;

public class ConsistencyIssueMapping : IEntityTypeConfiguration<ConsistencyIssue>
{
    public void Configure(EntityTypeBuilder<ConsistencyIssue> builder)
    {
        builder
            .HasOne(ci => ci.Audiobook)
            .WithMany()
            .HasForeignKey(ci => ci.AudiobookId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
