using System.ComponentModel.DataAnnotations;

namespace AudiobookManager.Api.Dtos;

/// <summary>The body of the bulk endpoints: the ids of the books the user selected explicitly.</summary>
public class BulkSelectionDto
{
    [Required] public List<long> AudiobookIds { get; set; } = null!;
}

/// <summary>One change to a free-text field. Action is "set" or "clear"; Value is ignored for "clear".</summary>
public class BulkEditSingleValueDto
{
    public string? Action { get; set; }
    public string? Value { get; set; }
}

/// <summary>One change to the Year field. Action is "set" or "clear"; Value is ignored for "clear".</summary>
public class BulkEditYearValueDto
{
    public string? Action { get; set; }
    public int? Value { get; set; }
}

/// <summary>
/// One change to a multi-value field. Action is "replace" (the list replaces the book's current
/// values), "add" (only values not already present are appended), or "clear" (the list is emptied
/// on every selected book - accepted for narrators and genres only; Authors refuses it).
/// </summary>
public class BulkEditMultiValueDto
{
    public string? Action { get; set; }
    public List<string>? Values { get; set; }
}

/// <summary>
/// The body of POST api/audiobook/bulk-edit. Every field-edit is optional and independent; a
/// field with no change object is left untouched by design - there is no implicit clearing.
/// </summary>
public class BulkEditAudiobooksRequestDto
{
    [Required] public List<long> AudiobookIds { get; set; } = null!;

    // Single-value fields, each optional:
    public BulkEditSingleValueDto? BookName { get; set; }
    public BulkEditSingleValueDto? Subtitle { get; set; }
    public BulkEditSingleValueDto? Series { get; set; }
    public BulkEditSingleValueDto? SeriesPart { get; set; }
    public BulkEditSingleValueDto? Description { get; set; }
    public BulkEditSingleValueDto? Copyright { get; set; }
    public BulkEditSingleValueDto? Publisher { get; set; }
    public BulkEditSingleValueDto? Language { get; set; }
    public BulkEditSingleValueDto? Rating { get; set; }
    public BulkEditSingleValueDto? Asin { get; set; }
    public BulkEditSingleValueDto? Www { get; set; }
    public BulkEditYearValueDto? Year { get; set; }
    public BulkEditMultiValueDto? Authors { get; set; }
    public BulkEditMultiValueDto? Narrators { get; set; }
    public BulkEditMultiValueDto? Genres { get; set; }
}

/// <summary>
/// One row of the bulk-edit preview: the selected books' current field values, so the user sees
/// exactly what the edit will touch before it runs.
/// </summary>
public record BulkEditPreviewItemDto(
    long Id,
    string? BookName,
    string? Subtitle,
    string? Series,
    string? SeriesPart,
    int? Year,
    List<string> Authors,
    List<string> Narrators,
    List<string> Genres,
    string? Description,
    string? Copyright,
    string? Publisher,
    string? Language,
    string? Rating,
    string? Asin,
    string? Www);

public class BulkEditPreviewResponseDto
{
    public List<BulkEditPreviewItemDto> Books { get; set; } = new();
}