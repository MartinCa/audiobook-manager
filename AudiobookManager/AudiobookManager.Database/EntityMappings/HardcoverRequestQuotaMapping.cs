using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;

public class HardcoverRequestQuotaMapping : IEntityTypeConfiguration<HardcoverRequestQuota>
{
    public void Configure(EntityTypeBuilder<HardcoverRequestQuota> builder)
    {
        builder
            .HasKey(q => q.UtcDate)
            .HasName("pk_hardcover_request_quota");

        builder
            .Property(q => q.RequestCount)
            .HasDefaultValue(0);
    }
}
