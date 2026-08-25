namespace AudiobookManager.Api.Dtos;

public class SimilarValueCandidateDto
{
    public string Value { get; set; }
    public int BookCount { get; set; }
    public List<long> AudiobookIds { get; set; }

    public SimilarValueCandidateDto(string value, int bookCount, List<long> audiobookIds)
    {
        Value = value;
        BookCount = bookCount;
        AudiobookIds = audiobookIds;
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

public class AlignSimilarValuesDto
{
    public string ValueType { get; set; } = string.Empty;
    public List<string> SourceValues { get; set; } = new();
    public string TargetValue { get; set; } = string.Empty;
}
