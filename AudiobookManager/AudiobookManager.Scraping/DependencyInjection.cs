using AudiobookManager.Scraping.Scrapers;
using AudiobookManager.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Scraping;
public static class DependencyInjection
{
    public static IServiceCollection SetupScraping(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddHttpClient("goodreads", client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        });
        services.AddHttpClient("hardcover")
            .ConfigureHttpClient((sp, client) =>
            {
                var settings = sp.GetRequiredService<IOptions<AudiobookManagerSettings>>().Value;
                if (!string.IsNullOrEmpty(settings.HardcoverApiKey))
                {
                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.HardcoverApiKey);
                }
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            });
        services.AddScoped<IBookSeriesMapper, BookSeriesMapper>();

        var scraperInterface = typeof(IScraper);
        typeof(IScraper).Assembly.GetTypes()
            .Where(x => !x.IsAbstract && x.IsClass && scraperInterface.IsAssignableFrom(x))
            .ToList()
            .ForEach(x => services.AddScoped(scraperInterface, x));

        return services;
    }

}
