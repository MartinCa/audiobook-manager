namespace AudiobookManager.Api.Dtos;

/// <summary>
/// The dropdown options for the book/author/series source filters, plus books' genre/language
/// filters - see BrowseController.GetFilterOptions.
/// </summary>
public record BrowseFilterOptionsDto(
    List<string> Sources,
    List<string> Genres,
    List<string> Languages
);
