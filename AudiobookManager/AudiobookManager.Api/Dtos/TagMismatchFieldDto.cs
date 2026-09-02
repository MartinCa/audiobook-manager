namespace AudiobookManager.Api.Dtos;

/// <summary>
/// One tag field that differs between the library metadata and the m4b's embedded tags, for the
/// selective tag-mismatch resolution screen. The field name and both candidate values are raw
/// strings (the same serialization the tag writer and <c>TagConsistencyChecker</c> use), so a
/// chosen value can be sent straight back to the resolve endpoint.
/// </summary>
public record TagMismatchFieldDto(
    string Field,
    string? LibraryValue,
    string? FileValue);
