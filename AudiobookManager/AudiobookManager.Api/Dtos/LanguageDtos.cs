namespace AudiobookManager.Api.Dtos;

/// <summary>
/// One selectable language. <paramref name="Aliases"/> carries every lowercased spelling that
/// folds to <paramref name="Code"/>, so the client folds scraped and tagged values exactly the
/// way the backend does instead of reimplementing the table and drifting from it.
/// </summary>
public record LanguageOptionDto(string Code, string DisplayName, List<string> Aliases);

/// <summary>
/// The language list the client renders its select from, plus the code a newly added book starts
/// on. Served from <see cref="AudiobookManager.Domain.Languages"/> so the frontend holds no list
/// of its own to drift from.
/// </summary>
public record LanguageOptionsDto(List<LanguageOptionDto> Languages, string DefaultCode);
