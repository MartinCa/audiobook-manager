using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;

public class SeriesEntityMapping : IEntityTypeConfiguration<Series>
{
    public void Configure(EntityTypeBuilder<Series> builder)
    {
        builder
            .HasKey(s => s.Id)
            .HasName("pk_series");

        builder
            .HasIndex(s => s.Name, "ix_series_name")
            .IsUnique();

        builder
            .HasMany(s => s.ExpectedBooks)
            .WithOne(b => b.Series)
            .HasForeignKey(b => b.SeriesId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
