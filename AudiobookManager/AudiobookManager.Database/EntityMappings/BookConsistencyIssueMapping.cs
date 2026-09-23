using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;

public class BookConsistencyIssueMapping : IEntityTypeConfiguration<BookConsistencyIssue>
{
    public void Configure(EntityTypeBuilder<BookConsistencyIssue> builder)
    {
        builder
            .HasOne(ci => ci.Audiobook)
            .WithMany()
            .HasForeignKey(ci => ci.AudiobookId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
