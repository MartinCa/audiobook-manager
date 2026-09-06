using System.Text.Json;

namespace AudiobookManager.Services;

/// <summary>
/// Serializes a TagMismatch issue's expected/actual values as a JSON array of
/// <c>{ field, value }</c> records, one element per differing field.
///
/// The previous format was newline-separated <c>"Field: value"</c> lines, which is ambiguous:
/// Description and Copyright are free-text and can legitimately contain a line that starts with
/// e.g. <c>"Publisher: "</c>, so a reader that splits on those markers truncates one field's
/// value and corrupts the next. JSON escapes newlines and any collision-prone text inside the
/// string, so the per-field boundary is exact. Issues stored before this format keep the legacy
/// blob in the database (they are only rewritten by the next consistency check); the frontend
/// parser falls back to the line-based reading for those.
/// </summary>
public static class TagMismatchPayload
{
    public sealed record FieldValue(string Field, string Value);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(IEnumerable<FieldValue> fields) =>
        JsonSerializer.Serialize(fields, JsonOptions);

    /// <summary>
    /// Parses a value written by <see cref="Serialize"/>. Returns null for anything that is not a
    /// JSON array of field records - callers treat that as the legacy line-based format.
    /// </summary>
    public static List<FieldValue>? TryParse(string? serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized) || !serialized.TrimStart().StartsWith('['))
        {
            return null;
        }

        try
        {
            var fields = JsonSerializer.Deserialize<List<FieldValue>>(serialized, JsonOptions);
            return fields is null || fields.Any(f => f.Field is null || f.Value is null)
                ? null
                : fields;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}