using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;

public class ExpectedBookAuthorMapping : IEntityTypeConfiguration<ExpectedBookAuthor>
{
    public void Configure(EntityTypeBuilder<ExpectedBookAuthor> builder)
    {
        builder
            .HasKey(l => l.Id)
            .HasName("pk_expected_book_authors");

        builder
            .HasIndex(l => l.ExpectedBookId, "ix_expected_book_authors_expected_book_id");

        builder
            .HasIndex(l => l.PersonId, "ix_expected_book_authors_person_id");

        builder
            .HasOne(l => l.Person)
            .WithMany()
            .HasForeignKey(l => l.PersonId)
            // SET NULL, not cascade: the link keeps its AuthorName when the Person row goes away,
            // so the roster stays readable (the name is the link's fallback identity) and a later
            // refresh can re-resolve the person.
            .OnDelete(DeleteBehavior.SetNull);
    }
}