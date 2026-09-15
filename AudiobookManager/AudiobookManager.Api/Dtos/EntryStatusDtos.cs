namespace AudiobookManager.Api.Dtos;

/// <summary>
/// One existing library value offered as a "similar" match for a typed author/series entry.
/// Id is populated for authors (they exist as Person rows with identity); series are free text
/// tag values with no identity of their own, so their matches carry a null Id.
/// </summary>
public record EntryMatchDto(long? Id, string Name);

/// <summary>
/// The bounded, server-side classification of a single typed author/series entry: "exact" when
/// the same value already exists in the library, "similar" when only near matches exist, "new"
/// when neither. Status is deliberately a string, not an enum, so it crosses the wire as itself
/// (this API does not register a string-enum converter; DESIGN.md wants string enums). The
/// matching is bounded: one input value, a capped candidate prefilter, a capped result.
/// </summary>
public record EntryStatusDto(
    string Value,
    string Status,
    EntryMatchDto? ExactMatch,
    List<EntryMatchDto> SimilarMatches);