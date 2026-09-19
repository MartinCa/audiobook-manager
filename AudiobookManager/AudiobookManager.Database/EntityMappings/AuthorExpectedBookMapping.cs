using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;

public class AuthorExpectedBookMapping : IEntityTypeConfiguration<AuthorExpectedBook>
{
    public void Configure(EntityTypeBuilder<AuthorExpectedBook> builder)
    {
        builder
            .HasKey(b => b.Id)
            .HasName("pk_author_expected_books");

        builder
            .HasIndex(b => b.PersonId, "ix_author_expected_books_person_id");

        builder
            .Property(b => b.IsIgnored)
            .HasDefaultValue(false);

        builder
            .HasOne(b => b.Person)
            .WithMany(p => p.ExpectedBooks)
            .HasForeignKey(b => b.PersonId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
