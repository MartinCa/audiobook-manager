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
        .AddScoped<ILibraryConsistencyService, LibraryConsistencyService>()
        .AddScoped<ISimilarValueService, SimilarValueService>()
        .AddScoped<IMissingTagService, MissingTagService>()
        .AddScoped<ISeriesService, SeriesService>()
        .SetupFileManager()
        .SetupScraping()
        .SetupDatabase();
}
