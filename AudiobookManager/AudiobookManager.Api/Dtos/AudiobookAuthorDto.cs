namespace AudiobookManager.Api.Dtos;

/// <summary>
/// An author reference on a book detail: identity for linking to the author's page. Additive to
/// <see cref="AudiobookDetailDto.Authors"/>, which stays a name-only list for existing consumers.
/// </summary>
public record AudiobookAuthorDto(long Id, string Name);