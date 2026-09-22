namespace AudiobookManager.Api.Dtos;

public class SimilarValueCandidateDto
{
    public string Value { get; set; }
    public int BookCount { get; set; }

    /// <summary>
    /// The matching <c>Person</c> row's id, for author candidates only - lets the client link
    /// straight to the author's page while investigating a group. Null for series candidates
    /// (series have no id, just a name) and for an author value that has no Person row.
    /// </summary>
    public long? AuthorId { get; set; }

    public SimilarValueCandidateDto(string value, int bookCount, long? authorId = null)
    {
        Value = value;
        BookCount = bookCount;
        AuthorId = authorId;
    }
}

public class SimilarValueGroupDto
{
    public List<SimilarValueCandidateDto> Candidates { get; set; }

    public SimilarValueGroupDto(List<SimilarValueCandidateDto> candidates)
    {
        Candidates = candidates;
    }
}

/// <summary>
/// One page of detected near-duplicate groups plus the total number of groups, so the client can
/// size its pager. The unpaged response returned every group with every candidate's full book
/// list; candidates now carry only their book count, and the groups are paged server-side.
/// </summary>
public record SimilarValueGroupsPageDto(List<SimilarValueGroupDto> Items, int TotalCount);

public class AlignSimilarValuesDto
{
    public string ValueType { get; set; } = string.Empty;
    public List<string> SourceValues { get; set; } = new();
    public string TargetValue { get; set; } = string.Empty;
}

/// <summary>One pair a user has marked as not similar, as returned by <c>GET api/similar-values/ignored</c>.</summary>
public record IgnoredSimilarValuePairDto(long Id, string ValueA, string ValueB, DateTime IgnoredAtUtc);

public class IgnoreSimilarValuePairDto
{
    public string ValueType { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public List<string> AgainstValues { get; set; } = new();
}
