using AudiobookManager.Database.Models;

namespace AudiobookManager.Services;

/// <summary>
/// Checks one aspect of an audiobook's on-disk state against its library metadata. Registered as
/// a DI collection (see DependencyInjection.SetupServiceLayer) and composed by
/// <see cref="IAudiobookIssueDetectionService"/>; each detector is independent and order between
/// them does not matter.
/// </summary>
public interface IConsistencyIssueDetector
{
    IEnumerable<ConsistencyIssue> Detect(AudiobookCheckContext context);
}
