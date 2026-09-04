using AudiobookManager.Database.Models;
using AudiobookManager.FileManager;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Services;

public class AudiobookIssueDetectionService : IAudiobookIssueDetectionService
{
    private readonly AudiobookManagerSettings _settings;
    private readonly IAudiobookTagHandler _tagHandler;
    private readonly IReadOnlyList<IConsistencyIssueDetector> _detectors;
    private readonly ILogger<AudiobookIssueDetectionService> _logger;

    public AudiobookIssueDetectionService(
        IOptions<AudiobookManagerSettings> settings,
        IAudiobookTagHandler tagHandler,
        IEnumerable<IConsistencyIssueDetector> detectors,
        ILogger<AudiobookIssueDetectionService> logger)
    {
        _settings = settings.Value;
        _tagHandler = tagHandler;
        _detectors = detectors.ToList();
        _logger = logger;
    }

    public List<ConsistencyIssue> DetectIssues(Audiobook audiobook)
    {
        if (!File.Exists(audiobook.FileInfoFullPath))
        {
            return new List<ConsistencyIssue>
            {
                ConsistencyIssueFactory.Create(audiobook.Id, ConsistencyIssueType.MissingMediaFile,
                    $"Media file not found: {audiobook.FileInfoFileName}",
                    audiobook.FileInfoFullPath, null)
            };
        }

        try
        {
            var fileInfo = new FileInfo(audiobook.FileInfoFullPath);
            // Only asks whether a cover exists (parsed.Cover is not null), never for its bytes -
            // encoding them for every book in the library is wasted work.
            var parsed = _tagHandler.ParseAudiobook(fileInfo, includeCoverData: false);
            var directoryPath = Path.GetDirectoryName(audiobook.FileInfoFullPath)!;
            var context = new AudiobookCheckContext(audiobook, parsed, directoryPath, _settings.AudiobookLibraryPath);

            return _detectors.SelectMany(detector => detector.Detect(context)).ToList();
        }
        catch (Exception ex)
        {
            // An empty list is indistinguishable from a clean book, which is how a corrupt m4b, a
            // file ATL cannot parse, or a permission-denied directory used to render on the
            // consistency screen: perfectly consistent. The only signal was a warning in the
            // container log, where nobody is looking while reading that screen.
            _logger.LogWarning(ex, "Failed to check consistency for {FilePath}", audiobook.FileInfoFullPath);

            return new List<ConsistencyIssue>
            {
                ConsistencyIssueFactory.Create(audiobook.Id, ConsistencyIssueType.UnreadableFile,
                    $"Media file could not be read: {audiobook.FileInfoFileName}",
                    audiobook.FileInfoFullPath, ex.Message)
            };
        }
    }
}
