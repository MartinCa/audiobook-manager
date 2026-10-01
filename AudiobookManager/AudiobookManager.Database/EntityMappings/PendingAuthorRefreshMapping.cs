using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;

public class PendingAuthorRefreshMapping : IEntityTypeConfiguration<PendingAuthorRefresh>
{
    public void Configure(EntityTypeBuilder<PendingAuthorRefresh> builder)
    {
        builder
            .HasOne(p => p.Person)
            .WithMany()
            .HasForeignKey(p => p.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        // At most one proposal per author - see the class doc comment.
        builder
            .HasIndex(p => p.PersonId)
            .IsUnique();
    }
}
