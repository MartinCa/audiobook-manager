namespace AudiobookManager.Domain;

/// <summary>
/// Edits to a book's series relations expressed on the domain model: the primary is
/// <see cref="Audiobook.Series"/>/<see cref="Audiobook.SeriesPart"/>, the rest live in
/// <see cref="Audiobook.AdditionalSeries"/>. Every caller that changes one series of a book
/// (assign, rename, remove, re-part) goes through these so the primary/secondary bookkeeping - a
/// deleted primary promoting the next relation, a rename colliding with an existing relation -
/// exists in one place. The result is always persisted through <c>AudiobookService.UpdateAudiobook</c>.
/// </summary>
public static class AudiobookSeriesExtensions
{
    private static bool SameName(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
        && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Every relation of the book, the primary first.</summary>
    public static List<(string Name, string? Part, bool IsPrimary)> AllSeries(this Audiobook book)
    {
        var all = new List<(string, string?, bool)>();
        if (!string.IsNullOrWhiteSpace(book.Series))
        {
            all.Add((book.Series.Trim(), book.SeriesPart, true));
        }

        foreach (var relation in book.AdditionalSeries ?? new List<SeriesRelation>())
        {
            all.Add((relation.Name, relation.Part, false));
        }

        return all;
    }

    /// <summary>The book's part in <paramref name="seriesName"/>, or null (also when it has no such relation).</summary>
    public static string? PartIn(this Audiobook book, string seriesName) =>
        book.AllSeries().FirstOrDefault(r => SameName(r.Name, seriesName)).Part;

    public static bool HasSeries(this Audiobook book, string seriesName) =>
        book.AllSeries().Any(r => SameName(r.Name, seriesName));

    /// <summary>
    /// Puts the book in <paramref name="seriesName"/> with <paramref name="part"/>. An existing
    /// relation just takes the new part. A book with no primary makes this its primary; a book
    /// that already has one keeps it and gains this as an additional series.
    /// </summary>
    public static void SetSeries(this Audiobook book, string seriesName, string? part)
    {
        var name = seriesName.Trim();
        if (SameName(book.Series, name))
        {
            book.SeriesPart = part;
            return;
        }

        book.AdditionalSeries ??= new List<SeriesRelation>();
        var index = book.AdditionalSeries.FindIndex(r => SameName(r.Name, name));
        if (index >= 0)
        {
            book.AdditionalSeries[index] = book.AdditionalSeries[index] with { Part = part };
        }
        else if (string.IsNullOrWhiteSpace(book.Series))
        {
            book.Series = name;
            book.SeriesPart = part;
        }
        else
        {
            book.AdditionalSeries.Add(new SeriesRelation(name, part));
        }
    }

    /// <summary>
    /// Removes the book's relation to <paramref name="seriesName"/>. Removing the primary
    /// promotes the first additional series (its part comes with it); with none left the book has
    /// no series. Returns whether the book had the relation.
    /// </summary>
    public static bool RemoveSeries(this Audiobook book, string seriesName)
    {
        if (SameName(book.Series, seriesName))
        {
            var next = book.AdditionalSeries?.FirstOrDefault();
            if (next is null)
            {
                book.Series = string.Empty;
                book.SeriesPart = null;
            }
            else
            {
                book.Series = next.Name;
                book.SeriesPart = next.Part;
                book.AdditionalSeries!.RemoveAt(0);
            }

            return true;
        }

        return book.AdditionalSeries?.RemoveAll(r => SameName(r.Name, seriesName)) > 0;
    }

    /// <summary>
    /// Renames one of the book's series. When the book already has a relation under the new name
    /// the two collapse into that one - which keeps its own part unless it had none, and stays
    /// primary if either was.
    /// </summary>
    public static void RenameSeries(this Audiobook book, string oldName, string newName)
    {
        var target = newName.Trim();
        if (!book.HasSeries(oldName))
        {
            return;
        }

        var collides = book.HasSeries(target) && !SameName(oldName, target);
        if (!collides)
        {
            if (SameName(book.Series, oldName))
            {
                book.Series = target;
            }
            else
            {
                var index = book.AdditionalSeries!.FindIndex(r => SameName(r.Name, oldName));
                book.AdditionalSeries[index] = book.AdditionalSeries[index] with { Name = target };
            }

            return;
        }

        var oldPart = book.PartIn(oldName);
        var oldWasPrimary = SameName(book.Series, oldName);
        var targetPart = book.PartIn(target);
        var mergedPart = string.IsNullOrWhiteSpace(targetPart) ? oldPart : targetPart;

        book.RemoveSeries(oldName);
        if (oldWasPrimary)
        {
            // The merged relation takes over the primary slot, wherever the target sat.
            book.RemoveSeries(target);
            var previousPrimary = book.Series;
            var previousPart = book.SeriesPart;
            book.Series = target;
            book.SeriesPart = mergedPart;
            if (!string.IsNullOrWhiteSpace(previousPrimary))
            {
                book.AdditionalSeries ??= new List<SeriesRelation>();
                book.AdditionalSeries.Insert(0, new SeriesRelation(previousPrimary, previousPart));
            }
        }
        else
        {
            book.SetSeries(target, mergedPart);
        }
    }
}
