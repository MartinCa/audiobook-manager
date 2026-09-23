using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;

public class AuthorConsistencyIssueMapping : IEntityTypeConfiguration<AuthorConsistencyIssue>
{
    public void Configure(EntityTypeBuilder<AuthorConsistencyIssue> builder)
    {
        builder
            .HasOne(i => i.Person)
            .WithMany()
            .HasForeignKey(i => i.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        // At most one row per author - see the class doc comment.
        builder
            .HasIndex(i => i.PersonId)
            .IsUnique();
    }
}
