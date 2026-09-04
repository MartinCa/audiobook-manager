using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;
public class PersonMapping : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder
            .HasIndex(p => p.Name)
            .IsUnique();

        // NameFolded maps by convention from its [Column] attribute - see the comment on
        // Audiobook.BookNameFolded for why it exists and is deliberately not indexed.
    }
}
