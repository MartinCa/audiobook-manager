namespace AudiobookManager.Api.Dtos;

public class MultiSearchDto
{
    public IList<string> Sources { get; set; } = new List<string>();
    public string Q { get; set; } = "";
}
