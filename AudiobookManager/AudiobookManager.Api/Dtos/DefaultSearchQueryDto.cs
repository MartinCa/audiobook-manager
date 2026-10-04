namespace AudiobookManager.Api.Dtos;

/// <summary>The book fields the default online-metadata search query is built from.</summary>
public record DefaultSearchQueryRequestDto(IReadOnlyList<string>? Authors, string? BookName, string? FileName);

/// <summary>The default search query, with the library's search-initials handling applied.</summary>
public record DefaultSearchQueryDto(string Query);
