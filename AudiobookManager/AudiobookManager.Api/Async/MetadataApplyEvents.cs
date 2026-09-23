namespace AudiobookManager.Api.Async;

/// <summary>Progress for a bulk "apply pending metadata changes" run (selected books, or every book matching a field filter).</summary>
public record MetadataApplyProgress(int Processed, int Total, int Succeeded, int Failed);

public record MetadataApplyComplete(int Processed, int Total, int Succeeded, int Failed);
