using AudiobookManager.Database.Models;
using AudiobookManager.Domain;
using AudiobookDomain = AudiobookManager.Domain.Audiobook;

namespace AudiobookManager.Services;

/// <summary>
/// Keeps a book's <c>audiobook_series</c> rows in step with its domain model, and mirrors the
/// primary one onto <c>audiobooks.series</c>/<c>series_part</c>. The one place that writes either,
/// so the mirror cannot drift from the relations (see AGENTS.md, "Series relations").
/// </summary>
public static class SeriesRelationSync
{
    /// <summary>One canonical relation: primary first, the rest in display order.</summary>
    public sealed record Desired(string Name, string? Part, bool IsPrimary, int SortOrder);

    /// <summary>
    /// The relation set a domain book asks for. Names are trimmed and blank ones dropped;
    /// a name repeated (case-insensitively) keeps its first occurrence, taking a later part only
    /// when the first had none. The primary is the domain's <c>Series</c>/<c>SeriesPart</c>.
    /// </summary>
    public static List<Desired> Normalize(string? primaryName, string? primaryPart, IEnumerable<SeriesRelation>? additional)
    {
        var result = new List<Desired>();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        void Add(string? name, string? part, bool isPrimary)
        {
            var trimmedName = name?.Trim();
            if (string.IsNullOrEmpty(trimmedName))
            {
                return;
            }

            var trimmedPart = string.IsNullOrWhiteSpace(part) ? null : part.Trim();
            if (seen.TryGetValue(trimmedName, out var index))
            {
                if (result[index].Part is null && trimmedPart is not null)
                {
                    result[index] = result[index] with { Part = trimmedPart };
                }

                return;
            }

            seen[trimmedName] = result.Count;
            result.Add(new Desired(trimmedName, trimmedPart, isPrimary, isPrimary ? 0 : result.Count));
        }

        Add(primaryName, primaryPart, true);
        foreach (var relation in additional ?? Enumerable.Empty<SeriesRelation>())
        {
            Add(relation.Name, relation.Part, false);
        }

        // Sort orders are dense and start at 0 for the primary (or 1 when the book has none).
        var order = 0;
        for (var i = 0; i < result.Count; i++)
        {
            result[i] = result[i] with { SortOrder = order++ };
        }

        return result;
    }

    /// <summary>
    /// Makes <paramref name="book"/>'s relation rows match <paramref name="audiobook"/> and mirrors
    /// the primary onto the book's own series columns. When the domain object's
    /// <c>AdditionalSeries</c> is null the stored non-primary relations are kept (minus one that
    /// the new primary now duplicates). Returns every series name the book had before or has now,
    /// so the caller can invalidate each one's cached reconciliation.
    /// </summary>
    public static HashSet<string> Apply(Database.Models.Audiobook book, AudiobookDomain audiobook)
    {
        var touched = new HashSet<string>(StringComparer.Ordinal);
        AddName(touched, book.Series);
        foreach (var r in book.SeriesRelations ?? new List<AudiobookSeries>())
        {
            AddName(touched, r.SeriesName);
        }

        var additional = audiobook.AdditionalSeries
            ?? (book.SeriesRelations ?? new List<AudiobookSeries>())
                .Where(r => !r.IsPrimary)
                .OrderBy(r => r.SortOrder)
                .Select(r => new SeriesRelation(r.SeriesName, r.SeriesPart))
                .ToList();

        var desired = Normalize(audiobook.Series, audiobook.SeriesPart, additional);
        var primary = desired.FirstOrDefault(d => d.IsPrimary);

        // The mirror: exactly what the domain's primary says, or nothing.
        book.Series = primary?.Name;
        book.SeriesPart = primary?.Part;

        var rows = book.SeriesRelations ??= new List<AudiobookSeries>();
        var byName = rows.ToDictionary(r => r.SeriesName, StringComparer.OrdinalIgnoreCase);

        // Rows whose name is no longer wanted are removed; the rest are updated in place below.
        foreach (var row in rows.ToList())
        {
            if (!desired.Any(d => string.Equals(d.Name, row.SeriesName, StringComparison.OrdinalIgnoreCase)))
            {
                rows.Remove(row);
            }
        }

        foreach (var d in desired)
        {
            AddName(touched, d.Name);
            if (byName.TryGetValue(d.Name, out var row) && rows.Contains(row))
            {
                row.SeriesName = d.Name;
                row.SeriesPart = d.Part;
                row.IsPrimary = d.IsPrimary;
                row.SortOrder = d.SortOrder;
            }
            else
            {
                rows.Add(new AudiobookSeries
                {
                    SeriesName = d.Name,
                    SeriesPart = d.Part,
                    IsPrimary = d.IsPrimary,
                    SortOrder = d.SortOrder,
                });
            }
        }

        return touched;
    }

    /// <summary>The non-primary relations of a loaded book, in display order; null when not loaded.</summary>
    public static List<SeriesRelation>? AdditionalOf(Database.Models.Audiobook book) =>
        book.SeriesRelations?
            .Where(r => !r.IsPrimary)
            .OrderBy(r => r.SortOrder)
            .Select(r => new SeriesRelation(r.SeriesName, r.SeriesPart))
            .ToList();

    /// <summary>Every series name a loaded book relates to (its primary mirror included).</summary>
    public static HashSet<string> AllNames(Database.Models.Audiobook book)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        AddName(names, book.Series);
        foreach (var r in book.SeriesRelations ?? new List<AudiobookSeries>())
        {
            AddName(names, r.SeriesName);
        }

        return names;
    }

    private static void AddName(HashSet<string> names, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            names.Add(name);
        }
    }
}
