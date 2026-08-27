namespace AudiobookManager.Services;

/// <summary>
/// Result of checking whether a file already occupies an audiobook's generated library path.
/// When <see cref="Exists"/> is true, the Existing* fields describe whatever is at that path -
/// a tracked library book when one matches, otherwise a plain filesystem stat of the file.
/// </summary>
public class TargetPathCollisionResult
{
    public required string TargetPath { get; init; }
    public bool Exists { get; init; }
    public long? ExistingAudiobookId { get; init; }
    public long? ExistingSizeInBytes { get; init; }
    public int? ExistingDurationInSeconds { get; init; }
}
