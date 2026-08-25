using Microsoft.AspNetCore.Mvc;

namespace AudiobookManager.Api.Async;

/// <summary>
/// Shared fire-and-forget orchestration for the "start a long-running operation, report
/// progress over SignalR, send a completion event" controller pattern used by consistency
/// checks and similar-value alignment.
/// </summary>
public static class BackgroundOperationRunner
{
    public static IActionResult Start(
        SemaphoreSlim gate,
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        Func<IServiceProvider, Task> work,
        Func<Task> onError)
    {
        if (!gate.Wait(0))
        {
            return new ConflictObjectResult("An operation is already in progress");
        }

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
                gate.Release();
            }
        });

        return new OkResult();
    }
}
