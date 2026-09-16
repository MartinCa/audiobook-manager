namespace AudiobookManager.Database.Search;

/// <summary>
/// The single source of truth for turning a user-controlled value into a byte-exact LIKE pattern.
/// Repositories must route every raw user-pattern interpolation through
/// <see cref="EscapeLikePattern"/> (and pass <see cref="EscapeCharacter"/> to
/// <c>EF.Functions.Like</c>) so a literal '%' or '_' the user typed matches rows containing
/// exactly that character instead of acting as a LIKE wildcard. Kept in exactly one place so a
/// future escape fix cannot be applied to one repository's copy and missed in another's.
/// </summary>
public static class LikePatterns
{
    public const string EscapeCharacter = "\\";

    public static string EscapeLikePattern(string? value) => (value ?? string.Empty)
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");
}