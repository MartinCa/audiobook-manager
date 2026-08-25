using AudiobookManager.Domain;

namespace AudiobookManager.Services;

public interface ISimilarValueService
{
    Task<List<SimilarValueGroup>> DetectSimilarAuthorsAsync();
    Task<List<SimilarValueGroup>> DetectSimilarSeriesAsync();

    Task<(int Processed, int Succeeded, int Failed)> AlignAuthorsAsync(
        List<string> sourceNames,
        string targetName,
        Func<int, int, int, int, Task> progressAction);

    Task<(int Processed, int Succeeded, int Failed)> AlignSeriesAsync(
        List<string> sourceValues,
        string targetValue,
        Func<int, int, int, int, Task> progressAction);
}
