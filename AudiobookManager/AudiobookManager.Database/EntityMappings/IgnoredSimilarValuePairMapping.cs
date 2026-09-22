using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;

public class IgnoredSimilarValuePairMapping : IEntityTypeConfiguration<IgnoredSimilarValuePair>
{
    public void Configure(EntityTypeBuilder<IgnoredSimilarValuePair> builder)
    {
        builder
            .HasKey(p => p.Id)
            .HasName("pk_ignored_similar_value_pairs");

        builder
            .HasIndex(p => new { p.Kind, p.ValueA, p.ValueB }, "ix_ignored_similar_value_pairs_kind_value_a_value_b")
            .IsUnique();
    }
}
