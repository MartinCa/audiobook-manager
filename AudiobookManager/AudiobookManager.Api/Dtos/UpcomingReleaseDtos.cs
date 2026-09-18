namespace AudiobookManager.Api.Dtos;

public record UpcomingReleaseDto(
    long Id,
    string Title,
    DateOnly ReleaseDate,
    long? AuthorId,
    string? AuthorName,
    long? SeriesId,
    string? SeriesName,
    string? SeriesPosition,
    string SourceName,
    string? SourceUrl,
    string? ImageUrl
);

public record AuthorFollowStatusDto(bool IsFollowed);

public record SeriesFollowStatusDto(bool IsFollowed);

public record AuthorMatchCandidateDto(
    string SourceId,
    string Name,
    string? SourceUrl,
    int? BookCount
);

public record AuthorMatchStatusDto(
    string? SourceId,
    string? SourceName,
    string? SourceUrl
);

public record MatchAuthorDto(string SourceId, string SourceName, string? SourceUrl);
