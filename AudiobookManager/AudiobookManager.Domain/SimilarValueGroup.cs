namespace AudiobookManager.Domain;

public class SimilarValueGroup
{
    public List<SimilarValueCandidate> Candidates { get; set; } = new();
}

/// <summary>
/// One value in a near-duplicate group. Carries the count of books using it, not the books
/// themselves: the list view only renders the count's magnitude, and alignment only needs the
/// values - the per-book lists are what made the detection payload grow with the library.
/// </summary>
public class SimilarValueCandidate
{
    public string Value { get; set; } = string.Empty;
    public int BookCount { get; set; }
}
