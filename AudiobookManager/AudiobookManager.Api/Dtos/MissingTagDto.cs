namespace AudiobookManager.Api.Dtos;

public record MissingTagFieldDto(string Key, string Label, bool IsCriticalByDefault);

public record AudiobookMissingTagsDto(long AudiobookId, string BookName, List<string> Authors, List<string> MissingFields);

/// <summary>
/// One page of audiobooks missing the selected tags plus the total matching that field set, so
/// the client can size its pager. Paged because the endpoint this replaces returned a book
/// missing even one selected critical field - thousands of rows on a large library - to render
/// into the DOM in one response.
/// </summary>
public record AudiobookMissingTagsPageDto(List<AudiobookMissingTagsDto> Items, int TotalCount);
