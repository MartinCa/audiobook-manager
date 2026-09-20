namespace AudiobookManager.Database.Repositories;

/// <summary>
/// The minimal per-book input the fuzzy roster reconciliation needs from an owned-book set: the
/// series value and part plus the book name, and the row id so an owned book matched to a roster
/// entry can be reported (and later fixed) when its stored part disagrees with the roster's
/// position. No authors, years or full entities - the missing/ignored/part-mismatch computation
/// compares these against roster entries, and it is the one read that touches every owned book of
/// a series (or an author), so everything else stays out of it.
///
/// <see cref="Series"/> is null/blank for a standalone book, and it lets the author reconciliation
/// scope a series entry's fuzzy matching to the owned books of the same local series value - the
/// series reconciliation (which reads one series at a time) does not need it and leaves it null.
/// </summary>
public record SeriesOwnedKey(long AudiobookId, string? SeriesPart, string BookName, string? Series)
{
    public SeriesOwnedKey(long audiobookId, string? seriesPart, string bookName)
        : this(audiobookId, seriesPart, bookName, null)
    {
    }
}