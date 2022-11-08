using AudiobookManager.Database;
using AudiobookManager.FileManager;
using AudiobookManager.Scraping;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookManager.Services;

public static class DependencyInjection
{
    public static IServiceCollection AddServices(this IServiceCollection services) => services
        .AddScoped<IFileService, FileService>()
        .AddScoped<IAudiobookService, AudiobookService>()
        .AddScoped<IScrapingService, ScrapingService>()
        .SetupFileManager()
        .SetupScraping()
        .SetupDatabase();
}
