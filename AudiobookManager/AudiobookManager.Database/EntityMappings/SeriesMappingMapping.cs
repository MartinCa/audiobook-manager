using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;
public class SeriesMappingMapping : IEntityTypeConfiguration<SeriesMapping>
{
    public void Configure(EntityTypeBuilder<SeriesMapping> builder)
    {
        builder
            .HasKey(x => x.Id)
            .HasName("pk_series_mapping");

        // A pattern is a deterministic first-match rewrite, so two patterns with the same regex
        // (each potentially routing the same incoming series value to a different owner) would be
        // ambiguous. The uniqueness is global, not per owner, on purpose.
        builder
            .HasIndex(u => u.Regex, "ix_series_mapping_regex")
            .IsUnique();

        builder
            .HasIndex(m => m.SeriesId, "ix_series_mapping_series_id");

        builder
            .HasOne(m => m.Series)
            .WithMany(s => s.Mappings)
            .HasForeignKey(m => m.SeriesId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
