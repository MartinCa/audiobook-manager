namespace AudiobookManager.Domain;

/// <summary>
/// One field a metadata refresh would change on a book, as computed by the refresh engine:
/// the book's current (library) value and the value the source reported. Free-form values are
/// carried as strings so the diff renders and applies uniformly; lists (authors, narrators,
/// genres) are serialized with the frontend's existing "/" list convention.
/// </summary>
public record MetadataRefreshDiff(string Field, string? LibraryValue, string? SourceValue);

/// <summary>
/// The result of refreshing one book's metadata from its online source. Either the fetched
/// snapshot (with the fields that would change, when any do), or the outcome of "nothing to do".
/// </summary>
public class MetadataRefreshResult
{
    /// <summary>True when the fetch succeeded (with or without differences); false on scrape failure.</summary>
    public required bool Success { get; init; }

    /// <summary>True when the source reported values that differ from the stored book.</summary>
    public bool HasDifferences { get; init; }

    /// <summary>The fields that would change, empty when none.</summary>
    public IReadOnlyList<MetadataRefreshDiff> Differences { get; init; } = Array.Empty<MetadataRefreshDiff>();

    /// <summary>The scraper that answered, when the fetch succeeded.</summary>
    public string? SourceName { get; init; }

    /// <summary>Why the refresh failed, when it did. Never shown to the user verbatim from an exception - see ProblemResults.</summary>
    public string? Error { get; init; }
}