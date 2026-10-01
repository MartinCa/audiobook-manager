namespace AudiobookManager.Api.Dtos;

/// <summary>
/// The name a matched source uses for an author that differs from the library's spelling,
/// pending the user's accept (rename the author and every book) or dismiss. ProposedName is
/// already formatted to the library's initials convention.
/// </summary>
public record AuthorRefreshPendingDto(
    long AuthorId,
    string AuthorName,
    string ProposedName,
    string SourceName,
    string? SourceUrl,
    DateTime FetchedAt);

/// <summary>A page of the pending author-name list (bounded; server-side paged).</summary>
public record AuthorRefreshPendingPageDto(
    List<AuthorRefreshPendingDto> Items,
    int TotalCount);
