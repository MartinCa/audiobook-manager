namespace AudiobookManager.Database.Repositories;

/// <summary>
/// One owned book of one author: the author's <see cref="PersonId"/> plus the same reduced
/// (series value, series part, book name, id) key <see cref="SeriesOwnedKey"/> carries. The pair
/// is what lets the bulk author-reconciliation owned-key read (<c>GetOwnedKeysByAuthorsAsync</c>)
/// serve every rostered author from ONE SQL query (join through the authors many-to-many) instead
/// of one query per author: the provider groups these by <see cref="PersonId"/> and feeds each
/// author's keys to the same <c>AuthorOwnedIndex</c> the single-author detail read does.
/// </summary>
public record AuthorOwnedKey(long PersonId, SeriesOwnedKey Key)
{
    public AuthorOwnedKey(long personId, long audiobookId, string? seriesPart, string bookName, string? series)
        : this(personId, new SeriesOwnedKey(audiobookId, seriesPart, bookName, series))
    {
    }
}