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
            .AddScoped<ILibrarySettingsRepository, LibrarySettingsRepository>()
            .AddScoped<IQueuedOrganizeTaskRepository, QueuedOrganizeTaskRepository>()
            .AddScoped<IDiscoveredAudiobookRepository, DiscoveredAudiobookRepository>()
            .AddScoped<IBookConsistencyIssueRepository, BookConsistencyIssueRepository>()
            .AddScoped<IPendingMetadataRefreshRepository, PendingMetadataRefreshRepository>()
            .AddScoped<IPendingSeriesRefreshRepository, PendingSeriesRefreshRepository>()
            .AddScoped<IOrphanDirectoryRepository, OrphanDirectoryRepository>()
            .AddScoped<ISeriesRepository, SeriesRepository>()
            .AddScoped<IHardcoverQuotaRepository, HardcoverQuotaRepository>()
            .AddScoped<IAuthorFollowRepository, AuthorFollowRepository>()
            .AddScoped<ISeriesFollowRepository, SeriesFollowRepository>()
            .AddScoped<IUpcomingReleaseRepository, UpcomingReleaseRepository>()
            .AddScoped<IExpectedBookRepository, ExpectedBookRepository>()
            .AddScoped<IScheduledTaskRunRepository, ScheduledTaskRunRepository>()
            .AddScoped<IIgnoredSimilarValuePairRepository, IgnoredSimilarValuePairRepository>();

}
