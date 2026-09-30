using AudiobookManager.Domain;

namespace AudiobookManager.Services;

/// <summary>
/// Writes a pending-refresh snapshot's fields onto a domain <see cref="Audiobook"/>, for the
/// fields the caller selects. This is the server-side counterpart of the client's TagPreviewDialog
/// apply logic - it exists so the bulk "apply selected books" / "apply matching filter" endpoints
/// and the single-book quick-apply endpoint can write a snapshot without a mounted edit form. Only
/// the fields named in <paramref name="fields"/> are touched; anything else on <paramref
/// name="book"/> is left exactly as loaded, matching the "no implicit clearing"
/// invariant <see cref="AudiobookBulkChanges"/> uses - with one deliberate exception: selecting
/// BookName while <paramref name="splitTitleOnColon"/> is on can also write Subtitle, when the
/// split recovers one out of an otherwise-blank snapshot (see the field-writing code below for
/// why - the split itself has nowhere else to put the recovered text). <paramref
/// name="splitTitleOnColon"/> gates <see cref="TitleSplitter"/> - off by default, since a
/// snapshot's BookName is the source's raw, unsplit title (see that class's remarks for why the
/// split is never assumed).
/// </summary>
public static class MetadataRefreshApplier
{
    public static void ApplyFields(
        Audiobook book,
        PendingRefreshPayload.Snapshot snapshot,
        IReadOnlySet<string> fields,
        bool splitTitleOnColon = false,
        string? primarySeriesName = null)
    {
        if (fields.Contains(MetadataRefreshFields.Authors))
        {
            book.Authors = snapshot.Authors.Select(name => new Person(name)).ToList();
        }

        if (fields.Contains(MetadataRefreshFields.Narrators))
        {
            book.Narrators = snapshot.Narrators.Select(name => new Person(name)).ToList();
        }

        var (splitBookName, splitSubtitle) = TitleSplitter.Apply(snapshot.BookName, snapshot.Subtitle, splitTitleOnColon);

        // The split has two cases. With a blank snapshot.Subtitle it carves the subtitle out of
        // BookName, so a non-null splitSubtitle can only be text the split itself just moved
        // there. Applying BookName without also writing that recovered half would silently
        // discard it, so this counts as implicitly covered by selecting BookName. With a
        // non-blank snapshot.Subtitle the split only strips a duplicate ": <subtitle>" tail from
        // BookName (the subtitle is already the snapshot's own); the toggle just changes the
        // proposed values, and whether BookName and/or Subtitle are applied stays the caller's
        // independent choice - a pre-existing snapshot.Subtitle still requires its own explicit
        // selection, so nothing is smuggled past the caller's selection.
        var subtitleRecoveredBySplit = splitTitleOnColon
            && string.IsNullOrWhiteSpace(snapshot.Subtitle)
            && !string.IsNullOrWhiteSpace(splitSubtitle);

        // The recovered subtitle only has somewhere to "come from" when BookName is also being
        // applied - otherwise the book's title keeps the full raw string (BookName unselected =
        // untouched) while Subtitle would still get the tail, duplicating the same text across
        // both fields instead of moving it. When BookName isn't selected, treat the split as if
        // it never happened for Subtitle's purposes: falls back to the pre-split, blank
        // snapshot.Subtitle - the clear/no-op the caller actually selected by picking Subtitle
        // alone.
        var subtitleToWrite = subtitleRecoveredBySplit && !fields.Contains(MetadataRefreshFields.BookName)
            ? snapshot.Subtitle
            : splitSubtitle;

        // BookName is non-nullable on the domain model; a snapshot always carries one (the
        // scraper result it was built from requires it), but a blank guard still keeps this
        // applier from ever handing the save pipeline a titleless book.
        if (fields.Contains(MetadataRefreshFields.BookName) && !string.IsNullOrWhiteSpace(splitBookName))
        {
            book.BookName = splitBookName;
        }

        if (fields.Contains(MetadataRefreshFields.Subtitle)
            || (subtitleRecoveredBySplit && fields.Contains(MetadataRefreshFields.BookName)))
        {
            book.Subtitle = subtitleToWrite;
        }

        // Series and its part are one field: the whole set of series the source reports replaces
        // the book's, with the primary chosen now (the caller's explicit choice, else the book's
        // current primary if the source still lists it - see SeriesRelationSet.ChoosePrimary).
        // The retired SeriesPart name is honoured as Series so an older selection cannot apply a
        // part without its series.
        if (fields.Contains(MetadataRefreshFields.Series) || fields.Contains(MetadataRefreshFields.SeriesPart))
        {
            var current = SeriesRelationSet.OfBook(book);
            SeriesRelationSet.ApplyTo(
                book,
                SeriesRelationSet.Resolve(current.All.ToList(), PendingRefreshPayload.SeriesOf(snapshot), primarySeriesName));
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

        // snapshot.Url is already the cleaned source URL (see MetadataRefreshDiffer's matching
        // comment). Writing it here is what lets a bulk-applied pending refresh actually record
        // which online source a book was matched to - MatchedSourceName then follows
        // automatically from AccentFoldedColumnsInterceptor on save, the same way it does for any
        // other write path that changes Www.
        if (fields.Contains(MetadataRefreshFields.Www))
        {
            book.Www = snapshot.Url;
        }
    }
}
