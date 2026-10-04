namespace AudiobookManager.Database.Models;

/// <summary>
/// The persisted value of the search-initials-handling library setting. Mirrored by
/// AudiobookManager.Domain's SearchInitialsHandling; the service layer maps between the two.
/// </summary>
public enum SearchInitialsHandling
{
    /// <summary>Author names are searched as stored.</summary>
    AsStored = 0,

    /// <summary>"George R.R. Martin".</summary>
    Compact = 1,

    /// <summary>"George R. R. Martin".</summary>
    Spaced = 2,
}
