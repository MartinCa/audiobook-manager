namespace AudiobookManager.Api;

/// <summary>
/// The paging limits every paged list endpoint shares - the cap on pageSize, the default when a
/// caller sends none, and the overflow guard on the <c>page * pageSize</c> skip. Keeping them in
/// one place means a page stays a page everywhere: an unbounded pageSize would turn a page into a
/// full export, and the offset guard stops the skip from overflowing int into a negative OFFSET
/// that SQLite silently reads as zero (serving the first page as though it were the requested one).
/// </summary>
public static class PagingLimits
{
    /// <summary>The largest page a caller may ask for. Beyond this the response stops being a page.</summary>
    public const int MaxPageSize = 200;

    /// <summary>The default page size for the paged endpoints when the caller does not send one.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>
    /// The furthest into the list a caller may ask to start.
    ///
    /// Bounded for two reasons. The offset is <c>page * pageSize</c>, which overflows a 32-bit int
    /// somewhere past page 10.7 million at the maximum page size - and the negative result does not
    /// fail, it is passed to SKIP, where SQLite reads a negative OFFSET as zero and silently serves
    /// the *first* page as though it were the requested one. Separately, even a valid enormous
    /// offset makes the database count its way there row by row. Twenty thousand default-sized
    /// pages is past any real library and well short of both problems.
    /// </summary>
    public const long MaxPageOffset = 1_000_000;

    /// <summary>
    /// The largest limit a caller may ask for on the limit-based search endpoints - tighter than
    /// <see cref="MaxPageSize"/>, because a search result is a preview, not a browse.
    /// </summary>
    public const int MaxSearchPageSize = 100;

    /// <summary>The furthest into a limit-based search result a caller may ask to start; same overflow guard as <see cref="MaxPageOffset"/>.</summary>
    public const long MaxSearchOffset = MaxPageOffset;
}