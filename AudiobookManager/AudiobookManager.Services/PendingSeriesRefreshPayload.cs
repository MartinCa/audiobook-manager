using System.Text.Json;
using AudiobookManager.Domain;

namespace AudiobookManager.Services;

/// <summary>
/// The stable, hand-versioned JSON contract for a pending series-refresh snapshot - the fetched
/// roster plus the explicit changes a refresh computed, stored in
/// <c>pending_series_refresh.payload_json</c> and re-read (possibly much) later, including after
/// a client upgrade that never saw the writer.
///
/// Deliberately distinct from the live <c>SeriesSearchResult</c>: that type is what a scraper
/// builds in-process, and its shape follows the scrapers' needs. Persisting it directly would let
/// a scraper-side change corrupt every stored snapshot. This record holds just what the review /
/// apply flow needs, in a shape owned by this feature. The roster is stored whole because the
/// apply replaces the stored roster with exactly what the user reviewed - re-fetching at apply
/// time would apply whatever the source says <em>then</em>, and an HTTP failure would block the
/// apply. <see cref="CurrentVersion"/> is stamped on every write so a future field addition can
/// be recognized and converted rather than silently misread.
/// </summary>
public static class PendingSeriesRefreshPayload
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>One fetched roster entry, in the shape the apply recreates it with.</summary>
    public sealed record RosterEntry(string? Position, string Title, int? Year, string? SourceUrl, bool IsCompilation);

    /// <summary>One explicit change, mirroring <see cref="Domain.SeriesRefreshChange"/>.</summary>
    public sealed record Change(
        SeriesRefreshChangeType Type,
        long? AudiobookId,
        string? BookName,
        string? StoredPart,
        string? NewPart,
        string? RosterTitle,
        string? Position,
        string? Title,
        int? Year);

    public sealed record Payload(
        int Version,
        string SeriesName,
        string SourceName,
        string SourceUrl,
        string? SourceSeriesName,
        DateTime FetchedAt,
        IReadOnlyList<RosterEntry> Roster,
        IReadOnlyList<Change> Changes);

    public static string Serialize(Payload payload) =>
        JsonSerializer.Serialize(payload, JsonOptions);

    /// <summary>
    /// Parses a payload written by <see cref="Serialize"/>, and by no other shape: null for a
    /// foreign/legacy JSON blob or a version newer than this build knows how to read, so a
    /// caller never renders half-converted data. A payload missing its version field deserializes
    /// as Version 0, which no writer ever produced - that is the "this is some other JSON" case.
    /// </summary>
    public static Payload? TryParse(string? serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(serialized, JsonOptions);
            return payload is null || payload.Version < 1 || payload.Version > CurrentVersion
                ? null
                : payload;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}