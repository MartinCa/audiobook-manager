namespace AudiobookManager.Services;

/// <summary>
/// A file already occupies the path an audiobook would be relocated to, and the caller did not ask
/// to replace it. Typed so an API layer can answer 409 with the message instead of a bare 500; the
/// message is the same text callers have always seen.
/// </summary>
public class TargetPathExistsException : Exception
{
    public string TargetPath { get; }

    public TargetPathExistsException(string targetPath)
        : base($"'{targetPath}' already exists")
    {
        TargetPath = targetPath;
    }
}
