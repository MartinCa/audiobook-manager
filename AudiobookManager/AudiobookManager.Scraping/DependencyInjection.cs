using AudiobookManager.Scraping.Scrapers;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookManager.Scraping;
public static class DependencyInjection
{
    public static IServiceCollection SetupScraping(this IServiceCollection services)
    {
        services
                .AddHttpClient()
                .AddScoped<IBookSeriesMapper, BookSeriesMapper>();

        var scraperInterface = typeof(IScraper);
        typeof(IScraper).Assembly.GetTypes()
            .Where(x => !x.IsAbstract && x.IsClass && scraperInterface.IsAssignableFrom(x))
            .ToList()
            .ForEach(x => services.AddScoped(scraperInterface, x));

        return services;
    }

}
