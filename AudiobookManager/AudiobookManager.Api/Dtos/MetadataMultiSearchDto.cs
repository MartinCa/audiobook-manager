namespace AudiobookManager.Api.Dtos;

public class MetadataMultiSearchDto
{
    public IList<string> Sources { get; set; } = new List<string>();
    public string Q { get; set; } = "";

    /// <summary>
    /// A query for a specific source, replacing <see cref="Q"/> for it (keys matched ignoring case).
    /// Sent while the search box still holds the unedited default query, which is written per
    /// source; a source with no entry is searched with <see cref="Q"/>.
    /// </summary>
    public Dictionary<string, string>? QueriesBySource { get; set; }
}
