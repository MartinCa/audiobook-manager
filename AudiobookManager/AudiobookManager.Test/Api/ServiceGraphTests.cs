using AudiobookManager.Api.Async;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Services;
using AudiobookManager.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Api;

/// <summary>
/// Validates the application's real service graph, which nothing else in this suite did: every
/// other test hands its subject mocks directly, so a registration the container cannot actually
/// construct is invisible to all of them.
///
/// That gap shipped a live one. PartMismatchIssueDetector took ISeriesService, SeriesService takes
/// ILibraryConsistencyService, and LibraryConsistencyService owns the detector - a cycle the
/// container refuses. Under Development's ValidateOnBuild the API did not start at all; under
/// Production it started and then 500'd every /api/series request, while the whole suite stayed
/// green. The cycle is broken by ISeriesReconciliationProvider; this is the guard that keeps any
/// future one from getting as far.
/// </summary>
[TestClass]
public class ServiceGraphTests
{
    /// <summary>
    /// The registrations Program.cs adds on top of SetupServiceLayer, so what is validated here
    /// is the container the app actually runs with rather than a subset of it.
    /// </summary>
    private static ServiceProvider BuildRealContainer()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSignalR();
        services.Configure<AudiobookManagerSettings>(_ => { });
        services.AddSingleton<IOperationStatusRegistry, OperationStatusRegistry>();
        // Supplied by the host at runtime, not by any of this app's own registrations. Stubbed so
        // what this test can fail on is this application's graph, not a missing host service.
        services.AddSingleton<IHostApplicationLifetime, StubApplicationLifetime>();
        services.SetupServiceLayer();

        // The same two validations Development turns on. ValidateOnBuild walks every registered
        // descriptor at build time, which is what turns a cycle into a build failure instead of a
        // 500 on the first request that happens to resolve through it.
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private sealed class StubApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }

    [TestMethod]
    public void ServiceGraph_EveryRegisteredService_CanBeConstructed()
    {
        using var provider = BuildRealContainer();

        Assert.IsNotNull(provider);
    }

    /// <summary>
    /// ValidateOnBuild skips open generics and anything registered by factory, so the services at
    /// the heart of the cycle are also resolved for real, in a scope, to prove the graph is
    /// constructible and not merely describable.
    /// </summary>
    [TestMethod]
    public void ServiceGraph_TheSeriesAndConsistencyServices_ResolveInAScope()
    {
        using var provider = BuildRealContainer();
        using var scope = provider.CreateScope();

        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<ISeriesService>());
        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<ILibraryConsistencyService>());
        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<IPartMismatchIssueDetector>());
        Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<ISeriesReconciliationProvider>());
    }

    /// <summary>
    /// The controllers are where a broken graph actually surfaced, so they are resolved too -
    /// SeriesController is the one whose every endpoint 500'd. Controllers are not registered by
    /// SetupServiceLayer (AddControllers activates them from the container without registering
    /// them), so they are activated the same way here.
    /// </summary>
    [TestMethod]
    public void ServiceGraph_TheControllersOnTheCycle_CanBeActivated()
    {
        using var provider = BuildRealContainer();
        using var scope = provider.CreateScope();

        foreach (var controllerType in new[]
                 {
                     typeof(SeriesController),
                     typeof(ConsistencyController),
                     typeof(AudiobookController),
                 })
        {
            Assert.IsNotNull(
                ActivatorUtilities.CreateInstance(scope.ServiceProvider, controllerType),
                $"{controllerType.Name} must be constructible from the real container");
        }
    }
}
