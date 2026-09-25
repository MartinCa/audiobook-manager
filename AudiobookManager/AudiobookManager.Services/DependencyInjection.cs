using AudiobookManager.Database;
using AudiobookManager.FileManager;
using AudiobookManager.Scraping;
using AudiobookManager.Services.Consistency;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookManager.Services;

public static class DependencyInjection
{
    public static IServiceCollection SetupServiceLayer(this IServiceCollection services) => services
        // Singleton, and backed by process-static state: the per-audiobook save gate has to
        // exclude across request scopes, which is the whole point of it.
        .AddSingleton<IAudiobookSaveGate, AudiobookSaveGate>()
        // Singleton: the expected-book write gate serializes every mutation of the unified
        // expected_books roster across the author and series controllers, so every caller -
        // request-scoped controller instance or background task - must see the same instance.
        .AddSingleton<IExpectedBookWriteGate, ExpectedBookWriteGate>()
        // Singletons: the near-duplicate group cache and the per-series detail reconciliation
        // cache are shared across requests (that is what makes their computations bounded), and
        // both sit behind a TTL plus explicit invalidation.
        .AddSingleton<ISimilarValueDetectionCache, SimilarValueDetectionCache>()
        .AddSingleton<ISeriesReconciliationCache, SeriesReconciliationCache>()
        // Stateless and cheap: a singleton avoids re-resolving it per request for what is
        // effectively a pure function over the bytes.
        .AddSingleton<ICoverImageProcessor, CoverImageProcessor>()
        .AddScoped<IFileService, FileService>()
        .AddScoped<IAudiobookService, AudiobookService>()
        .AddScoped<IScrapingService, ScrapingService>()
        .AddScoped<ISettingsService, SettingsService>()
        .AddScoped<IScheduledTaskService, ScheduledTaskService>()
        .AddScoped<IQueuedOrganizeTaskService, QueuedOrganizeTaskService>()
        .AddScoped<ILibraryScanService, LibraryScanService>()
        .AddScoped<ILibraryScanOrchestrator, LibraryScanOrchestrator>()
        .SetupConsistencyServices()
        .AddScoped<ILibraryConsistencyService, LibraryConsistencyService>()
        .AddScoped<ISimilarValueService, SimilarValueService>()
        .AddScoped<IMissingTagService, MissingTagService>()
        .AddScoped<IUrlCleanupService, UrlCleanupService>()
        .AddScoped<ILanguageBackfillService, LanguageBackfillService>()
        .AddScoped<ISeriesReconciliationProvider, SeriesReconciliationProvider>()
        .AddScoped<IAuthorReconciliationProvider, AuthorReconciliationProvider>()
        .AddScoped<ISeriesService, SeriesService>()
        .AddScoped<IMetadataRefreshService, MetadataRefreshService>()
        .AddScoped<IPendingOnlineMatchService, PendingOnlineMatchService>()
        .AddScoped<IBulkEditService, BulkEditService>()
        .AddScoped<IUpcomingReleaseService, UpcomingReleaseService>()
        .SetupFileManager()
        .SetupScraping()
        .SetupDatabase();
}
