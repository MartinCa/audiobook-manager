using System.Text.Json;

namespace AudiobookManager.Services;

/// <summary>
/// The field-name vocabulary shared by <see cref="MetadataRefreshDiffer"/> (what changed),
/// <see cref="PendingMetadataRefresh.ChangedFieldsJson"/> (what was stored as changed at fetch
/// time) and <see cref="MetadataRefreshApplier"/> (what a caller may selectively apply). Keeping
/// one constant per field name here is what stops the three from drifting - a diff the differ
/// stops reporting, or a field the applier renames, would otherwise silently break the stored
/// "changed fields" list on old rows or the filter UI's vocabulary.
/// </summary>
public static class MetadataRefreshFields
{
    public const string Authors = "Authors";
    public const string Narrators = "Narrators";
    public const string BookName = "BookName";
    public const string Subtitle = "Subtitle";
    public const string Series = "Series";
    /// <summary>
    /// Retired as a field of its own: <see cref="Series"/> now covers every series a book has with
    /// their parts and the primary, so a part can no longer be applied apart from its series. Kept
    /// only so a stored changed-fields list or an older client that still names it is understood
    /// as <see cref="Series"/>; the differ never emits it.
    /// </summary>
    public const string SeriesPart = "SeriesPart";
    public const string Year = "Year";
    public const string Genres = "Genres";
    public const string Description = "Description";
    public const string Language = "Language";
    public const string Rating = "Rating";
    public const string Copyright = "Copyright";
    public const string Publisher = "Publisher";
    public const string Asin = "Asin";
    public const string Www = "Www";

    /// <summary>Every field name a pending snapshot can offer as a change, in display order.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Authors, Narrators, BookName, Subtitle, Series, Year, Genres, Description,
        Language, Rating, Copyright, Publisher, Asin, Www,
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Parses <see cref="Database.Models.PendingMetadataRefresh.ChangedFieldsJson"/> back into a
    /// field-name list; an old row written before that column existed, or unparseable JSON,
    /// yields an empty list rather than throwing - the caller reads it for display/filtering, not
    /// as data it must trust unconditionally.
    /// </summary>
    public static List<string> ParseChangedFieldsJson(string? changedFieldsJson)
    {
        if (string.IsNullOrWhiteSpace(changedFieldsJson))
        {
            return new List<string>();
        }

        try
        {
            // A list stored before SeriesPart was folded into Series names it; read it as Series.
            return (JsonSerializer.Deserialize<List<string>>(changedFieldsJson, JsonOptions) ?? new List<string>())
                .Select(f => f == SeriesPart ? Series : f)
                .Distinct()
                .ToList();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }
}
