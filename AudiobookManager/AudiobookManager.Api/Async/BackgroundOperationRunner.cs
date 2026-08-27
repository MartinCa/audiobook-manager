using Microsoft.AspNetCore.Mvc;

namespace AudiobookManager.Api.Async;

/// <summary>
/// Shared fire-and-forget orchestration for the "start a long-running operation, report
/// progress over SignalR, send a completion event" controller pattern used by library
/// scanning, discovered-book import, consistency checks, similar-value alignment, and series
/// matching/refresh. Also records the operation's status in <see cref="IOperationStatusRegistry"/>
/// so a client can recover the current state on mount or after a SignalR reconnect.
/// </summary>
public static class BackgroundOperationRunner
{
    public static IActionResult Start(
        SemaphoreSlim gate,
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        IOperationStatusRegistry statusRegistry,
        string operationKey,
        Func<IServiceProvider, Task> work,
        Func<Task> onError,
        CancellationToken applicationStopping = default)
    {
        if (!gate.Wait(0))
        {
            return new ConflictObjectResult("An operation is already in progress");
        }

        statusRegistry.SetRunning(operationKey);

        // The fire-and-forget work below doesn't (yet) thread a CancellationToken into `work`
        // itself - most of the underlying service methods it calls don't accept one, and
        // retrofitting that across every long-running operation is out of scope here. As a
        // minimally invasive improvement, at least surface a clear signal when the host shuts
        // down while an operation is still in flight, so an unexpected abrupt stop is visible in
        // the logs instead of silent.
        var shutdownRegistration = applicationStopping.CanBeCanceled
            ? applicationStopping.Register(() =>
                logger.LogWarning(
                    "Application is stopping while background operation '{OperationKey}' is still running; it will not be cancelled and may be interrupted mid-way",
                    operationKey))
            : (CancellationTokenRegistration?)null;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await work(scope.ServiceProvider);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during background operation");
                try
                {
                    await onError();
                }
                catch (Exception hubEx)
                {
                    logger.LogError(hubEx, "Failed to send completion notification over SignalR");
                }
            }
            finally
            {
                shutdownRegistration?.Dispose();
                statusRegistry.SetFinished(operationKey);
                gate.Release();
            }
        });

        return new OkResult();
    }
}
