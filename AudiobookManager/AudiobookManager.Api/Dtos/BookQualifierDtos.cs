namespace AudiobookManager.Api.Dtos;

/// <summary>
/// One selectable book qualifier. <paramref name="Key"/> is the stable identifier stored on a book
/// and sent back on save; <paramref name="Label"/> is what the user sees and what appears in the
/// parenthetical suffix (<paramref name="Suffix"/>) written to disk.
/// </summary>
public record BookQualifierDto(string Key, string Label, string Suffix);

/// <summary>
/// The qualifiers the client renders its badges and edit control from, in the order they should
/// be offered. Served from <see cref="AudiobookManager.Domain.BookQualifiers"/> so the frontend
/// holds no list of its own to drift from.
/// </summary>
public record BookQualifierOptionsDto(List<BookQualifierDto> Qualifiers);
