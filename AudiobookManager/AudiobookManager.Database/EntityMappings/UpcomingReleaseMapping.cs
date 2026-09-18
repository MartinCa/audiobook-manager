using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;

public class UpcomingReleaseMapping : IEntityTypeConfiguration<UpcomingRelease>
{
    public void Configure(EntityTypeBuilder<UpcomingRelease> builder)
    {
        builder
            .HasKey(r => r.Id)
            .HasName("pk_upcoming_releases");

        // A release is discovered at most once per source book, whether the poll that found it
        // was following the author, the series, or (on a later poll) both.
        builder
            .HasIndex(r => new { r.SourceName, r.SourceBookId }, "ix_upcoming_releases_source")
            .IsUnique();

        builder
            .HasIndex(r => r.PersonId, "ix_upcoming_releases_person_id");

        builder
            .HasIndex(r => r.SeriesId, "ix_upcoming_releases_series_id");

        builder
            .HasOne(r => r.Person)
            .WithMany()
            .HasForeignKey(r => r.PersonId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(r => r.Series)
            .WithMany()
            .HasForeignKey(r => r.SeriesId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
