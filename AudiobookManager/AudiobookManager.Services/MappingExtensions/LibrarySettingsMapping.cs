using AudiobookManager.Domain;

namespace AudiobookManager.Services.MappingExtensions;

public static class LibrarySettingsMapping
{
    public static Domain.LibrarySettings ToDomain(this Database.Models.LibrarySettings dbModel) =>
        new() { InitialsSpacing = ToDomain(dbModel.InitialsSpacing) };

    public static Database.Models.InitialsSpacing ToDb(this Domain.InitialsSpacing domain) => domain switch
    {
        Domain.InitialsSpacing.Spaced => Database.Models.InitialsSpacing.Spaced,
        Domain.InitialsSpacing.Unspaced => Database.Models.InitialsSpacing.Unspaced,
        _ => throw new ArgumentOutOfRangeException(nameof(domain), domain, "Unknown initials spacing"),
    };

    public static Domain.InitialsSpacing ToDomain(this Database.Models.InitialsSpacing db) => db switch
    {
        Database.Models.InitialsSpacing.Spaced => Domain.InitialsSpacing.Spaced,
        Database.Models.InitialsSpacing.Unspaced => Domain.InitialsSpacing.Unspaced,
        _ => throw new ArgumentOutOfRangeException(nameof(db), db, "Unknown initials spacing"),
    };
}
