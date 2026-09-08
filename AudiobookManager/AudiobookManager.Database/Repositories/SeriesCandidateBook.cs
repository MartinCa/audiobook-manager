namespace AudiobookManager.Database.Repositories;

/// <summary>
/// A library audiobook reduced to the fields the missing-book candidate search needs - projected
/// in SQL rather than loaded whole, mirroring <see cref="SeriesGroupingBook"/>.
/// </summary>
public record SeriesCandidateBook(long Id, string BookName, string? Series, string? SeriesPart, int Year, List<string> Authors);
