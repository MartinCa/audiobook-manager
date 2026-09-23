using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;

public class SeriesConsistencyIssueMapping : IEntityTypeConfiguration<SeriesConsistencyIssue>
{
    public void Configure(EntityTypeBuilder<SeriesConsistencyIssue> builder)
    {
        builder
            .HasOne(i => i.Series)
            .WithMany()
            .HasForeignKey(i => i.SeriesId)
            .OnDelete(DeleteBehavior.Cascade);

        // At most one row per series - see the class doc comment.
        builder
            .HasIndex(i => i.SeriesId)
            .IsUnique();
    }
}
