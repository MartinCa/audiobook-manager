using AudiobookManager.Database;
using AudiobookManager.FileManager;
using AudiobookManager.Scraping;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookManager.Services;

public static class DependencyInjection
{
    public static IServiceCollection SetupServiceLayer(this IServiceCollection services) => services
        // Singleton, and backed by process-static state: the per-audiobook save gate has to
        // exclude across request scopes, which is the whole point of it.
        .AddSingleton<IAudiobookSaveGate, AudiobookSaveGate>()
        .AddScoped<IFileService, FileService>()
        .AddScoped<IAudiobookService, AudiobookService>()
        .AddScoped<IScrapingService, ScrapingService>()
        .AddScoped<ISettingsService, SettingsService>()
        .AddScoped<IQueuedOrganizeTaskService, QueuedOrganizeTaskService>()
        .AddScoped<ILibraryScanService, LibraryScanService>()
        // Consistency issue detection and resolution, split into one detector/resolver per
        // concern and registered as DI collections; LibraryConsistencyService dispatches to them
        // rather than switching on ConsistencyIssueType itself. See AudiobookCheckContext,
        // IConsistencyIssueDetector and IConsistencyIssueResolver for how they're composed.
        .AddSingleton<IConsistencyIssueDetector, PathMismatchDetector>()
        .AddSingleton<IConsistencyIssueDetector, TagMismatchDetector>()
        .AddSingleton<IConsistencyIssueDetector, SidecarFilesDetector>()
        .AddSingleton<IConsistencyIssueDetector, CoverFileDetector>()
        .AddScoped<IAudiobookIssueDetectionService, AudiobookIssueDetectionService>()
        .AddScoped<IConsistencyIssueResolver, MissingMediaFileResolver>()
        .AddScoped<IConsistencyIssueResolver, MetadataSidecarResolver>()
        .AddScoped<IConsistencyIssueResolver, TagOrPathMismatchResolver>()
        .AddScoped<IConsistencyIssueResolver, MissingCoverResolver>()
        .AddScoped<IOrphanDirectoryConsistencyService, OrphanDirectoryConsistencyService>()
        .AddScoped<ILibraryConsistencyService, LibraryConsistencyService>()
        .AddScoped<ISimilarValueService, SimilarValueService>()
        .AddScoped<IMissingTagService, MissingTagService>()
        .AddScoped<ILanguageBackfillService, LanguageBackfillService>()
        .AddScoped<ISeriesService, SeriesService>()
        .SetupFileManager()
        .SetupScraping()
        .SetupDatabase();
}
