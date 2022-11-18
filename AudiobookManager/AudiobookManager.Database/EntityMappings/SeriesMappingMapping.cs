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

        builder
            .HasIndex(u => u.Regex, "ix_series_mapping_regex")
            .IsUnique();
    }
}
