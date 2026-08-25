namespace AudiobookManager.Domain;

public class SimilarValueGroup
{
    public List<SimilarValueCandidate> Candidates { get; set; } = new();
}

public class SimilarValueCandidate
{
    public string Value { get; set; } = string.Empty;
    public List<SimilarValueBook> Books { get; set; } = new();
}

public class SimilarValueBook
{
    public long Id { get; set; }
    public string BookName { get; set; } = string.Empty;
}
