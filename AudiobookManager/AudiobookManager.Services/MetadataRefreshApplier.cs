using AudiobookManager.Domain;

namespace AudiobookManager.Services;

/// <summary>
/// Writes a pending-refresh snapshot's fields onto a domain <see cref="Audiobook"/>, for the
/// fields the caller selects. This is the server-side counterpart of the client's TagPreviewDialog
/// apply logic - it exists so the bulk "apply selected books" / "apply matching filter" endpoints
/// and the single-book quick-apply endpoint can write a snapshot without a mounted edit form. Only
/// the fields named in <paramref name="fields"/> are touched; anything else on <paramref
/// name="book"/> is left exactly as loaded, matching the "no implicit clearing"
/// invariant <see cref="AudiobookBulkChanges"/> uses.
/// </summary>
public static class MetadataRefreshApplier
{
    public static void ApplyFields(
        Audiobook book,
        PendingRefreshPayload.Snapshot snapshot,
        IReadOnlySet<string> fields)
    {
        if (fields.Contains(MetadataRefreshFields.Authors))
        {
            book.Authors = snapshot.Authors.Select(name => new Person(name)).ToList();
        }

        if (fields.Contains(MetadataRefreshFields.Narrators))
        {
            book.Narrators = snapshot.Narrators.Select(name => new Person(name)).ToList();
        }

        // BookName is non-nullable on the domain model; a snapshot always carries one (the
        // scraper result it was built from requires it), but a blank guard still keeps this
        // applier from ever handing the save pipeline a titleless book.
        if (fields.Contains(MetadataRefreshFields.BookName) && !string.IsNullOrWhiteSpace(snapshot.BookName))
        {
            book.BookName = snapshot.BookName;
        }

        if (fields.Contains(MetadataRefreshFields.Subtitle))
        {
            book.Subtitle = snapshot.Subtitle;
        }

        if (fields.Contains(MetadataRefreshFields.Series))
        {
            book.Series = snapshot.SeriesName;
        }

        if (fields.Contains(MetadataRefreshFields.SeriesPart))
        {
            book.SeriesPart = snapshot.SeriesPart;
        }

        // Same "never blank a missing source year" rule the differ applies: an absent snapshot
        // year is never offered as a diff, so it must never be applied as one either.
        if (fields.Contains(MetadataRefreshFields.Year) && snapshot.Year.HasValue)
        {
            book.Year = snapshot.Year.Value;
        }

        if (fields.Contains(MetadataRefreshFields.Genres))
        {
            book.Genres = snapshot.Genres.ToList();
        }

        if (fields.Contains(MetadataRefreshFields.Description))
        {
            book.Description = snapshot.Description;
        }

        if (fields.Contains(MetadataRefreshFields.Language))
        {
            book.Language = Languages.Normalize(snapshot.Language) ?? snapshot.Language ?? book.Language;
        }

        if (fields.Contains(MetadataRefreshFields.Rating))
        {
            book.Rating = snapshot.Rating;
        }

        if (fields.Contains(MetadataRefreshFields.Copyright))
        {
            book.Copyright = snapshot.Copyright;
        }

        if (fields.Contains(MetadataRefreshFields.Publisher))
        {
            book.Publisher = snapshot.Publisher;
        }

        if (fields.Contains(MetadataRefreshFields.Asin))
        {
            book.Asin = snapshot.Asin;
        }
    }
}
