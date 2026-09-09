namespace AudiobookManager.Api.Async;

public record BulkEditProgress(int Processed, int Total, int Succeeded, int Failed);

public record BulkEditComplete(int Processed, int Succeeded, int Failed);