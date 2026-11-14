namespace AudiobookManager.Api.Dtos;

/// <summary>
/// The book fields the default online-metadata search query is built from. <paramref name="MainAuthor"/>,
/// when given, replaces the whole author list in the query. <paramref name="SourceNames"/> are the
/// sources to build a query for; omitted means every registered source.
/// </summary>
public record DefaultSearchQueryRequestDto(
    IReadOnlyList<string>? Authors,
    string? BookName,
    string? FileName,
    string? MainAuthor = null,
    IReadOnlyList<string>? SourceNames = null);

/// <summary>
/// The default search query. <paramref name="Query"/> is written with the library-wide initials
/// handling; <paramref name="QueriesBySource"/> holds the one for each source, written with the
/// handling that applies to it (they differ only where a source has its own setting).
/// </summary>
public record DefaultSearchQueryDto(string Query, IReadOnlyDictionary<string, string>? QueriesBySource = null);
