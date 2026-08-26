namespace AudiobookManager.Services;

public record MissingTagField(string Key, string Label, bool IsCriticalByDefault);

public record AudiobookMissingTags(long AudiobookId, string BookName, List<string> Authors, List<string> MissingFields);

public interface IMissingTagService
{
    List<MissingTagField> GetTaggableFields();

    Task<List<AudiobookMissingTags>> FindAudiobooksMissingTagsAsync(IEnumerable<string> fieldKeys);
}
