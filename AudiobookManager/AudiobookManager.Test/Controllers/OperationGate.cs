using System.Reflection;
using AudiobookManager.Services;

namespace AudiobookManager.Test.Controllers;

/// <summary>
/// Test helper for the fire-and-forget controller endpoints that guard themselves with a
/// process-wide gate (see BackgroundOperationRunner).
///
/// A gate outlives a single test: if one test returns before the background task's finally
/// block has run the release, the next test to hit the same endpoint gets a
/// ConflictObjectResult instead of an OkResult. Sleeping a fixed amount to "let it finish" is a
/// race under CI thread-pool contention - this polls the real condition (the gate being free
/// again) instead, so it returns as soon as the release has happened and only fails if it
/// genuinely never does.
/// </summary>
internal static class OperationGate
{
    /// <summary>
    /// Waits until the shared expected-book write gate is unheld (back to acquirable), i.e. the
    /// background roster mutation that held it has run its finally. Polls with TryAcquire
    /// (releasing immediately) because the gate abstraction exposes no count.
    /// </summary>
    public static async Task WaitUntilReleasedAsync(IExpectedBookWriteGate gate, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (gate.TryAcquire())
            {
                gate.Release();
                return;
            }

            await Task.Delay(1);
        }

        Assert.Fail("Expected-book write gate was still held after the timeout; a previous operation never released it.");
    }

    /// <summary>
    /// Waits until every static gate declared on <paramref name="controllerType"/> is
    /// unheld, i.e. back to its initial count of 1. Only for the controllers that still carry
    /// static <see cref="SemaphoreSlim"/> gates; the expected-book roster mutations are guarded
    /// by the shared injected gate instead (see <see cref="WaitUntilReleasedAsync(IExpectedBookWriteGate, TimeSpan?)"/>).
    /// </summary>
    public static async Task WaitUntilReleasedAsync(Type controllerType, TimeSpan? timeout = null)
    {
        var gates = controllerType
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(SemaphoreSlim))
            .Select(f => (SemaphoreSlim)f.GetValue(null)!)
            .ToList();

        Assert.IsTrue(
            gates.Count > 0,
            $"{controllerType.Name} declares no static SemaphoreSlim gate - has the guard pattern changed?");

        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (gates.All(g => g.CurrentCount == 1))
            {
                return;
            }

            await Task.Delay(1);
        }

        Assert.Fail(
            $"Background operation gate on {controllerType.Name} was still held after the timeout; "
            + "a previous operation never released it.");
    }
}
