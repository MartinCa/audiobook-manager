using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;

public class GenreMapping : IEntityTypeConfiguration<Genre>
{
    public void Configure(EntityTypeBuilder<Genre> builder)
    {
        // Genres are resolved by name, exactly as persons are, so the name has to be unique for
        // that to mean anything. Without the index a "get or create" race silently produced two
        // rows for the same genre - each with its own set of linked books - which then made
        // GetOrCreateGenre's SingleOrDefaultAsync throw.
        builder
            .HasIndex(g => g.Name)
            .IsUnique();
    }
}
