using System.Reflection;

namespace AudiobookManager.Test.Controllers;

/// <summary>
/// Test helper for the fire-and-forget controller endpoints that guard themselves with a
/// process-static <see cref="SemaphoreSlim"/> gate (see BackgroundOperationRunner).
///
/// Because the gate is static, it outlives a single test: if one test returns before the
/// background task's finally block has run <c>gate.Release()</c>, the next test to hit the same
/// endpoint gets a ConflictObjectResult instead of an OkResult. Sleeping a fixed amount to
/// "let it finish" is a race under CI thread-pool contention - this polls the real condition
/// (the gate being free again) instead, so it returns as soon as the release has happened and
/// only fails if it genuinely never does.
/// </summary>
internal static class OperationGate
{
    /// <summary>
    /// Waits until every static gate declared on <paramref name="controllerType"/> is
    /// unheld, i.e. back to its initial count of 1.
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
