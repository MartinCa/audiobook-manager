namespace AudiobookManager.Domain;

public class SimilarValueGroup
{
    public List<SimilarValueCandidate> Candidates { get; set; } = new();
}

public class SimilarValueCandidate
{
    public string Value { get; set; } = string.Empty;
    public List<long> AudiobookIds { get; set; } = new();
}
