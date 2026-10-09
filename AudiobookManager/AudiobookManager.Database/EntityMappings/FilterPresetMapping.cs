using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;

public class FilterPresetMapping : IEntityTypeConfiguration<FilterPreset>
{
    public void Configure(EntityTypeBuilder<FilterPreset> builder)
    {
        // "Unsupported only" and "unsupported only" are the same name to a person, so the unique
        // index compares ignoring (ASCII) case, like the qualifier-indicator rules.
        builder
            .Property(p => p.Name)
            .UseCollation("NOCASE");

        builder
            .HasIndex(p => new { p.Scope, p.Name }, "ix_filter_preset_scope_name")
            .IsUnique();
    }
}
