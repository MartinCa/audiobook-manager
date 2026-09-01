namespace AudiobookManager.Services;

public record OrphanDirectoryResolveResult(
    long Id,
    string DirectoryPath,
    string ActionTaken,
    string Message
);
