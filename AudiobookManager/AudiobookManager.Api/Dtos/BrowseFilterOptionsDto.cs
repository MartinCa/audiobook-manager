namespace AudiobookManager.Api.Dtos;

/// <summary>One option of a "Queue status" filter: the wire value and the text shown for it.</summary>
public record QueueStateOptionDto(string Value, string Label);

/// <summary>
/// The dropdown options for the book/author/series source filters, plus books' genre/language
/// filters and each list's "Queue status" options - see BrowseController.GetFilterOptions.
/// </summary>
public record BrowseFilterOptionsDto(
    List<string> Sources,
    List<string> Genres,
    List<string> Languages,
    List<QueueStateOptionDto> BookQueueStates,
    List<QueueStateOptionDto> SeriesQueueStates,
    List<QueueStateOptionDto> AuthorQueueStates
);
