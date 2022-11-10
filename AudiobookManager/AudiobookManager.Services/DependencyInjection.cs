using AudiobookManager.Database;
using AudiobookManager.FileManager;
using AudiobookManager.Scraping;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookManager.Services;

public static class DependencyInjection
{
    public static IServiceCollection SetupServiceLayer(this IServiceCollection services) => services
        .AddScoped<IFileService, FileService>()
        .AddScoped<IAudiobookService, AudiobookService>()
        .AddScoped<IScrapingService, ScrapingService>()
        .AddScoped<ISettingsService, SettingsService>()
        .SetupFileManager()
        .SetupScraping()
        .SetupDatabase();
}
