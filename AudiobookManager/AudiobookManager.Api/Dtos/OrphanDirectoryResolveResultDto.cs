namespace AudiobookManager.Api.Dtos;

public record OrphanDirectoryResolveResultDto(
    long Id,
    string DirectoryPath,
    string ActionTaken,
    string Message
);
