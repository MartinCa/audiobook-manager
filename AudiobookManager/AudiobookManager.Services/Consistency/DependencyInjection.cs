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
    /// switching on ConsistencyIssueType itself. See AudiobookCheckContext,
    /// IConsistencyIssueDetector and IConsistencyIssueResolver for how they're composed.
    /// </summary>
    public static IServiceCollection SetupConsistencyServices(this IServiceCollection services) => services
        .AddSingleton<IConsistencyIssueDetector, PathMismatchDetector>()
        .AddSingleton<IConsistencyIssueDetector, TagMismatchDetector>()
        .AddSingleton<IConsistencyIssueDetector, SidecarFilesDetector>()
        .AddSingleton<IConsistencyIssueDetector, CoverFileDetector>()
        .AddScoped<IAudiobookIssueDetectionService, AudiobookIssueDetectionService>()
        .AddScoped<IConsistencyIssueResolver, MissingMediaFileResolver>()
        .AddScoped<IConsistencyIssueResolver, LibraryPathUnavailableResolver>()
        .AddScoped<IConsistencyIssueResolver, MetadataSidecarResolver>()
        .AddScoped<IConsistencyIssueResolver, TagOrPathMismatchResolver>()
        .AddScoped<IConsistencyIssueResolver, MissingCoverResolver>()
        .AddScoped<IConsistencyIssueResolver, UnreadableFileResolver>()
        .AddScoped<IOrphanDirectoryConsistencyService, OrphanDirectoryConsistencyService>();
}
