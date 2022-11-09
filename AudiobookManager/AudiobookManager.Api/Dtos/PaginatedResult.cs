namespace AudiobookManager.Api.Dtos;

public class PaginatedResult<T>
{
    public int Count { get; set; }
    public int Total { get; set; }
    public IList<T> Items { get; set; }

    public PaginatedResult(int count, int total, IList<T> items)
    {
        Count = count;
        Total = total;
        Items = items;
    }
}
