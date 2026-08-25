using AudiobookManager.Domain;

namespace AudiobookManager.Services;

public interface ISimilarValueService
{
    Task<List<SimilarValueGroup>> DetectSimilarAuthorsAsync();
    Task<List<SimilarValueGroup>> DetectSimilarSeriesAsync();

    Task AlignAuthorsAsync(
        List<string> sourceNames,
        string targetName,
        Func<int, int, int, int, Task> progressAction);

    Task AlignSeriesAsync(
        List<string> sourceValues,
        string targetValue,
        Func<int, int, int, int, Task> progressAction);
}
