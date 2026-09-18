using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;

public class AuthorFollowMapping : IEntityTypeConfiguration<AuthorFollow>
{
    public void Configure(EntityTypeBuilder<AuthorFollow> builder)
    {
        builder
            .HasKey(f => f.Id)
            .HasName("pk_author_follows");

        builder
            .HasIndex(f => f.PersonId, "ix_author_follows_person_id")
            .IsUnique();

        builder
            .HasOne(f => f.Person)
            .WithMany()
            .HasForeignKey(f => f.PersonId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
