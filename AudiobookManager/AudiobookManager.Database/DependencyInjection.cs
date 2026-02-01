using AudiobookManager.Database.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookManager.Database;
public static class DependencyInjection
{
    public static IServiceCollection SetupDatabase(this IServiceCollection services) =>
        services
            .AddDbContext<DatabaseContext>()
            .AddScoped<IAudiobookRepository, AudiobookRepository>()
            .AddScoped<IPersonRepository, PersonRepository>()
            .AddScoped<IGenreRepository, GenreRepository>()
            .AddScoped<ISeriesMappingRepository, SeriesMappingRepository>()
            .AddScoped<IQueuedOrganizeTaskRepository, QueuedOrganizeTaskRepository>()
            .AddScoped<IDiscoveredAudiobookRepository, DiscoveredAudiobookRepository>()
            .AddScoped<IConsistencyIssueRepository, ConsistencyIssueRepository>();

}
