namespace AudiobookManager.Api.Dtos;

public class SimilarValueCandidateDto
{
    public string Value { get; set; }
    public int BookCount { get; set; }

    public SimilarValueCandidateDto(string value, int bookCount)
    {
        Value = value;
        BookCount = bookCount;
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
