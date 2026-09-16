using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;

public class PendingSeriesRefreshMapping : IEntityTypeConfiguration<PendingSeriesRefresh>
{
    public void Configure(EntityTypeBuilder<PendingSeriesRefresh> builder)
    {
        builder
            .HasIndex(p => p.SeriesName)
            .IsUnique();
    }
}