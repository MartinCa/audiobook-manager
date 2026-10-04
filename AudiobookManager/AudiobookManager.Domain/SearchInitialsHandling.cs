namespace AudiobookManager.Domain;

/// <summary>
/// How the run of initials in an author's name is written in the DEFAULT online-metadata search
/// query (the one seeded for the manual search dialog and used by the bulk search). Sources index
/// names in one specific form and tokenize the query, so "George R. R. Martin" can miss a book
/// that "George R.R. Martin" finds on Hardcover. It never touches a stored name, tag or path, and
/// text the user types into the search box themselves is always sent exactly as typed.
/// </summary>
public enum SearchInitialsHandling
{
    /// <summary>The author names are searched exactly as the library stores them.</summary>
    AsStored = 0,

    /// <summary>Initials are dotted with no space between them: "George R.R. Martin".</summary>
    Compact = 1,

    /// <summary>Initials are dotted with a space between them: "George R. R. Martin".</summary>
    Spaced = 2,
}
