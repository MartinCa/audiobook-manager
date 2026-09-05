using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudiobookManager.Database.EntityMappings;

public class LibrarySettingsMapping : IEntityTypeConfiguration<LibrarySettings>
{
    public void Configure(EntityTypeBuilder<LibrarySettings> builder)
    {
        builder
            .HasKey(x => x.Id)
            .HasName("pk_library_settings");
    }
}
