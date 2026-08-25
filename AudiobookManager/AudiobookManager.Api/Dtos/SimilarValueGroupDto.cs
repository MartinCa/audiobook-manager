namespace AudiobookManager.Api.Dtos;

public class SimilarValueBookDto
{
    public long Id { get; set; }
    public string BookName { get; set; }

    public SimilarValueBookDto(long id, string bookName)
    {
        Id = id;
        BookName = bookName;
    }
}

public class SimilarValueCandidateDto
{
    public string Value { get; set; }
    public int BookCount { get; set; }
    public List<SimilarValueBookDto> Books { get; set; }

    public SimilarValueCandidateDto(string value, List<SimilarValueBookDto> books)
    {
        Value = value;
        BookCount = books.Count;
        Books = books;
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
