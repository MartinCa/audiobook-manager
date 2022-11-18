using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;
public class AudiobookMapping : IEntityTypeConfiguration<Audiobook>
{
    public void Configure(EntityTypeBuilder<Audiobook> builder)
    {
        builder
            .HasMany(a => a.Authors)
            .WithMany(p => p.BooksAuthored)
            .UsingEntity("audiobooks_authors_persons");

        builder
            .HasMany(a => a.Narrators)
            .WithMany(p => p.BooksNarrated)
            .UsingEntity("audiobooks_narrators_persons");

        builder
            .HasMany(a => a.Genres)
            .WithMany(g => g.Books);
    }
}
