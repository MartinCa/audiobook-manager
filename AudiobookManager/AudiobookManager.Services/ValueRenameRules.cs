using AudiobookManager.Domain;

namespace AudiobookManager.Services;

/// <summary>
/// What a manual rename of an author or series value may be renamed to. Returns the message the
/// caller relays as a 400 (these are safe to show - they tell the user what to change), or null
/// when the rename is acceptable. Shared by the controller (checked before the background rename
/// starts, so a bad request fails synchronously) and the service (defensively, so no other caller
/// can write a name that cannot round-trip through the tags).
/// </summary>
public static class ValueRenameRules
{
    public static string? ValidateAuthor(string? oldName, string? newName)
    {
        var common = ValidateCommon(oldName, newName, "author");
        if (common is not null)
        {
            return common;
        }

        // Authors are written to the tag as one ", "-joined string and split on the comma when
        // read back, so a name containing one would come back as two authors - the save's
        // round-trip check would then refuse every later save of that book.
        if (newName!.Contains(','))
        {
            return "An author name cannot contain a comma: authors are stored comma-separated in the file's tags, so it would be read back as two authors.";
        }

        return null;
    }

    public static string? ValidateSeries(string? oldName, string? newName)
    {
        var common = ValidateCommon(oldName, newName, "series");
        if (common is not null)
        {
            return common;
        }

        // Series are stored clean and the qualifier suffix is only added when files are written
        // (see BookQualifiers). Renaming to a name that already carries one would store a
        // suffixed series, which then gets a second suffix on every qualified book. A book's own
        // qualifiers decide the suffix, so the right place to change them is the book.
        var trimmed = newName!.Trim();
        var qualifier = BookQualifiers.All.FirstOrDefault(q =>
            trimmed.EndsWith(q.Suffix.TrimStart(), StringComparison.OrdinalIgnoreCase) &&
            trimmed.Length > q.Suffix.TrimStart().Length);
        if (qualifier is not null)
        {
            return $"Series names are stored without qualifiers: leave off \"({qualifier.Label})\" - it is added from each book's own qualifiers when its files are written.";
        }

        return null;
    }

    private static string? ValidateCommon(string? oldName, string? newName, string kind)
    {
        if (string.IsNullOrWhiteSpace(oldName))
        {
            return $"The current {kind} name is required.";
        }

        if (string.IsNullOrWhiteSpace(newName))
        {
            return $"The new {kind} name is required.";
        }

        if (string.Equals(oldName.Trim(), newName.Trim(), StringComparison.Ordinal))
        {
            return $"The new {kind} name is the same as the current one.";
        }

        return null;
    }
}
