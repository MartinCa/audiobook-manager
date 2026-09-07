using System.Text.Json;

namespace AudiobookManager.Services;

/// <summary>
/// The stable, hand-versioned JSON contract for a pending metadata-refresh snapshot - the values
/// a scraper returned for a book's source URL, stored in <c>pending_metadata_refresh.payload_json</c>
/// and re-read (possibly much) later, including after a client upgrade that never saw the writer.
///
/// Deliberately distinct from the live <c>MetadataSearchResult</c>: that type is what a scraper
/// builds in-process, and its shape follows the scrapers' needs. Persisting it directly would let
/// a scraper-side change corrupt every stored snapshot. This record holds just what the diff /
/// approval flow needs, in a shape owned by this feature. <see cref="Version"/> is stamped on
/// every write so a future field addition can be recognized and converted rather than silently
/// misread. Person/genre lists are plain strings, matching how <see
/// cref="Domain.Audiobook"/> consumes them.
/// </summary>
public static class PendingRefreshPayload
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public sealed record Snapshot(
        int Version,
        string Url,
        string Source,
        IReadOnlyList<string> Authors,
        IReadOnlyList<string> Narrators,
        string BookName,
        string? Subtitle,
        string? SeriesName,
        string? SeriesPart,
        int? Year,
        IReadOnlyList<string> Genres,
        string? Description,
        string? Language,
        string? Rating,
        string? Copyright,
        string? Publisher,
        string? Asin);

    public static string Serialize(Snapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, JsonOptions);

    /// <summary>
    /// Parses a payload written by <see cref="Serialize"/>, and by no other shape: null for a
    /// foreign/legacy JSON blob or a version newer than this build knows how to read, so a
    /// caller never renders half-converted data. A payload missing its version field deserializes
    /// as Version 0, which no writer ever produced - that is the "this is some other JSON" case.
    /// </summary>
    public static Snapshot? TryParse(string? serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return null;
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<Snapshot>(serialized, JsonOptions);
            return snapshot is null || snapshot.Version < 1 || snapshot.Version > CurrentVersion
                ? null
                : snapshot;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}