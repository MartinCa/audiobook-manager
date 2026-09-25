using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudiobookManager.Services;

/// <summary>
/// The stable, hand-versioned JSON contract for a pending online-match row's candidate results -
/// stored in <c>pending_online_match.results_json</c> and re-read (possibly much) later, including
/// after a client upgrade that never saw the writer.
///
/// Deliberately reuses <see cref="PendingRefreshPayload.Snapshot"/> for each candidate rather than
/// inventing a parallel shape: a selected candidate is handed straight to
/// <see cref="MetadataRefreshService.ApplyFetchedResultAsSnapshotAsync"/> after a fresh
/// <c>GetBookDetails</c> fetch, and the frontend's existing snapshot-to-search-result conversion
/// (<c>pendingSnapshotToSearchResult</c>) renders a candidate the same way it renders a pending
/// metadata-refresh snapshot - one shape, one rendering path, no drift between the two features.
/// </summary>
public static class PendingOnlineMatchPayload
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public sealed record Envelope(int Version, IReadOnlyList<PendingRefreshPayload.Snapshot> Results);

    public static string Serialize(IReadOnlyList<PendingRefreshPayload.Snapshot> results) =>
        JsonSerializer.Serialize(new Envelope(CurrentVersion, results), JsonOptions);

    /// <summary>
    /// Parses a payload written by <see cref="Serialize"/>. Returns an empty list (not null) for a
    /// blank/foreign/unreadable payload or a future version this build cannot read, so a caller
    /// never has to null-check before rendering - the row simply shows no candidates left to
    /// choose from, which is also the correct rendering for a genuinely empty search.
    /// </summary>
    public static IReadOnlyList<PendingRefreshPayload.Snapshot> Parse(string? serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return Array.Empty<PendingRefreshPayload.Snapshot>();
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(serialized, JsonOptions);
            return envelope is null || envelope.Version < 1 || envelope.Version > CurrentVersion
                ? Array.Empty<PendingRefreshPayload.Snapshot>()
                : envelope.Results ?? Array.Empty<PendingRefreshPayload.Snapshot>();
        }
        catch (JsonException)
        {
            return Array.Empty<PendingRefreshPayload.Snapshot>();
        }
    }
}
