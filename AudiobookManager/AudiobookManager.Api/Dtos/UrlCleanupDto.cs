namespace AudiobookManager.Api.Dtos;

public record AudiobookUrlCleanupDto(long AudiobookId, string BookName, List<string> Authors, string CurrentUrl, string CleanedUrl);

public record ApplyUrlCleanupDto(List<long> AudiobookIds);

public record ApplyUrlCleanupResultDto(int Updated);
