namespace AudiobookManager.Api.Dtos;

public record MissingTagFieldDto(string Key, string Label, bool IsCriticalByDefault);

public record AudiobookMissingTagsDto(long AudiobookId, string BookName, List<string> Authors, List<string> MissingFields);
