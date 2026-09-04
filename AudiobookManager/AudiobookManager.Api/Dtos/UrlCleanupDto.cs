namespace AudiobookManager.Api.Dtos;

public record AudiobookUrlCleanupDto(long AudiobookId, string BookName, List<string> Authors, string CurrentUrl, string CleanedUrl);

/// <summary>
/// One page of dirty URLs plus the total number of dirty books, so the client can size its pager
/// without asking again.
///
/// Paged because the endpoint this replaces returned every dirty URL inline, and a library with a
/// few thousand of them made that a multi-megabyte response, parsed and then held for the session,
/// to render every card into the DOM at once.
/// </summary>
public record UrlCleanupPageDto(List<AudiobookUrlCleanupDto> Items, int TotalCount);

public record ApplyUrlCleanupDto(List<long> AudiobookIds);

public record ApplyUrlCleanupResultDto(int Updated);
