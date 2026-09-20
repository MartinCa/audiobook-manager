namespace AudiobookManager.Scraping;

/// <summary>
/// Thrown by <c>Scrapers.IScraper.GetAuthorBooks</c> when the source cannot resolve the author
/// the caller asked for: the source id does not parse into what the source expects, or the
/// source returns no such author (deleted/merged upstream, or a transient empty response).
/// A roster refresh must treat this as a FAILED fetch and leave the stored roster untouched -
/// catching this lets <c>UpcomingReleaseService.RefreshAuthorRosterCoreAsync</c> abort before
/// the prune that an empty fetch would trigger, which would otherwise delete the author's whole
/// unified roster, ignore history included.
/// </summary>
public sealed class AuthorNotFoundException : Exception
{
    public AuthorNotFoundException(string message)
        : base(message)
    {
    }
}