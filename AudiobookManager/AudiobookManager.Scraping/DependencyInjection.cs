using Microsoft.Extensions.DependencyInjection;

namespace AudiobookManager.Scraping;
public static class DependencyInjection
{
    public static IServiceCollection SetupScraping(this IServiceCollection services) =>
        services
        .AddHttpClient()
        .AddScoped<IAudibleScraper, AudibleScraper>()
        .AddScoped<IBookSeriesMapper, BookSeriesMapper>();
}
