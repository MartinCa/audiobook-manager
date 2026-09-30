using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;
public class QualifierIndicatorMapping : IEntityTypeConfiguration<QualifierIndicator>
{
    public void Configure(EntityTypeBuilder<QualifierIndicator> builder)
    {
        builder
            .HasKey(x => x.Id)
            .HasName("pk_qualifier_indicator");

        // One meaning per wording per source, whatever the casing: two rules for the same
        // indicator would make the resulting qualifier depend on row order.
        builder
            .Property(x => x.Source)
            .UseCollation("NOCASE");
        builder
            .Property(x => x.Indicator)
            .UseCollation("NOCASE");

        builder
            .HasIndex(x => new { x.Source, x.Indicator }, "ix_qualifier_indicator_source_indicator")
            .IsUnique();
    }
}
