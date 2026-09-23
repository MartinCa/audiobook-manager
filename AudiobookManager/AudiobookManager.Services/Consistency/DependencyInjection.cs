using Microsoft.Extensions.DependencyInjection;

// The detectors, resolvers and services this method registers are declared in
// AudiobookManager.Services (flat, alongside LibraryConsistencyService), not in this namespace -
// only this registration file lives under Consistency/ to keep SetupServiceLayer uncluttered.
using AudiobookManager.Services;

namespace AudiobookManager.Services.Consistency;

public static class DependencyInjection
{
    /// <summary>
    /// Consistency issue detection and resolution, split into one detector/resolver per concern
    /// and registered as DI collections; LibraryConsistencyService dispatches to them rather than
    /// switching on BookConsistencyIssueType itself. See AudiobookCheckContext,
    /// IBookConsistencyIssueDetector and IBookConsistencyIssueResolver for how they're composed.
    /// </summary>
    public static IServiceCollection SetupConsistencyServices(this IServiceCollection services) => services
        .AddSingleton<IBookConsistencyIssueDetector, PathMismatchDetector>()
        .AddSingleton<IBookConsistencyIssueDetector, TagMismatchDetector>()
        .AddSingleton<IBookConsistencyIssueDetector, SidecarFilesDetector>()
        .AddSingleton<IBookConsistencyIssueDetector, CoverFileDetector>()
        .AddScoped<IInitialsSpacingIssueDetector, InitialsSpacingIssueDetector>()
        .AddScoped<IPartMismatchIssueDetector, PartMismatchIssueDetector>()
        .AddScoped<IAudiobookIssueDetectionService, AudiobookIssueDetectionService>()
        .AddScoped<IBookConsistencyIssueResolver, MissingMediaFileResolver>()
        .AddScoped<IBookConsistencyIssueResolver, LibraryPathUnavailableResolver>()
        .AddScoped<IBookConsistencyIssueResolver, MetadataSidecarResolver>()
        .AddScoped<IBookConsistencyIssueResolver, TagOrPathMismatchResolver>()
        .AddScoped<IBookConsistencyIssueResolver, SeriesPartMismatchResolver>()
        .AddScoped<IBookConsistencyIssueResolver, MissingCoverResolver>()
        .AddScoped<IBookConsistencyIssueResolver, UnreadableFileResolver>()
        .AddScoped<IBookConsistencyIssueResolver, InitialsSpacingResolver>()
        .AddScoped<IBookConsistencyIssueResolver, MetadataRefreshFailedResolver>()
        .AddScoped<IOrphanDirectoryConsistencyService, OrphanDirectoryConsistencyService>();
}
