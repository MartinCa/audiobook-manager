using AudiobookManager.Domain;

namespace AudiobookManager.Services.MappingExtensions;

public static class LibrarySettingsMapping
{
    public static Domain.LibrarySettings ToDomain(this Database.Models.LibrarySettings dbModel) =>
        new()
        {
            InitialsSpacing = ToDomain(dbModel.InitialsSpacing),
            InitialsPunctuation = ToDomain(dbModel.InitialsPunctuation),
            MetadataRefreshDelayMs = dbModel.MetadataRefreshDelayMs,
            UpcomingReleasesEnabled = dbModel.UpcomingReleasesEnabled,
            UpcomingReleasesCronSchedule = dbModel.UpcomingReleasesCronSchedule,
        };

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

    public static Database.Models.InitialsPunctuation ToDb(this Domain.InitialsPunctuation domain) => domain switch
    {
        Domain.InitialsPunctuation.Dotted => Database.Models.InitialsPunctuation.Dotted,
        Domain.InitialsPunctuation.Undotted => Database.Models.InitialsPunctuation.Undotted,
        _ => throw new ArgumentOutOfRangeException(nameof(domain), domain, "Unknown initials punctuation"),
    };

    public static Domain.InitialsPunctuation ToDomain(this Database.Models.InitialsPunctuation db) => db switch
    {
        Database.Models.InitialsPunctuation.Dotted => Domain.InitialsPunctuation.Dotted,
        Database.Models.InitialsPunctuation.Undotted => Domain.InitialsPunctuation.Undotted,
        _ => throw new ArgumentOutOfRangeException(nameof(db), db, "Unknown initials punctuation"),
    };
}
