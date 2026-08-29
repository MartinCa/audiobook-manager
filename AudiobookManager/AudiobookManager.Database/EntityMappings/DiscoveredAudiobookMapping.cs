using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;

public class DiscoveredAudiobookMapping : IEntityTypeConfiguration<DiscoveredAudiobook>
{
    public void Configure(EntityTypeBuilder<DiscoveredAudiobook> builder)
    {
        // Discovered rows are addressed by path, not id: DeleteByPathAsync (one per row the
        // user dismisses) and GetByPathsAsync (once per bulk import) both filter on it, and a
        // freshly scanned library can hold tens of thousands of rows.
        builder
            .HasIndex(d => d.FileInfoFullPath, "ix_discovered_audiobooks_file_info_full_path");
    }
}
