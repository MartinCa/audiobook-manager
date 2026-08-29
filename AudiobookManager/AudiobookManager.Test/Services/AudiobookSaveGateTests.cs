using AudiobookManager.Services;
using AudiobookManager.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookManager.Test.Services;

[TestClass]
public class AudiobookSaveGateTests
{
    private const long BookId = 1;
    private const long OtherBookId = 2;

    [TestMethod]
    public void TryAcquire_WhileAnotherOperationHoldsTheBook_Fails()
    {
        var gate = new AudiobookSaveGate();

        Assert.IsTrue(gate.TryAcquire(BookId, out var first));
        using (first)
        {
            Assert.IsTrue(gate.IsBusy(BookId));
            Assert.IsFalse(gate.TryAcquire(BookId, out _));
        }

        Assert.IsFalse(gate.IsBusy(BookId));
        Assert.IsTrue(gate.TryAcquire(BookId, out var afterRelease));
        afterRelease.Dispose();
    }

    // The gate is per book, not global: a bulk operation must not be able to block every other
    // book in the library while it works through one.
    [TestMethod]
    public void TryAcquire_ForADifferentBook_IsUnaffected()
    {
        var gate = new AudiobookSaveGate();

        using var lease = gate.Acquire(BookId);

        Assert.IsTrue(gate.TryAcquire(OtherBookId, out var otherLease));
        otherLease.Dispose();
    }

    [TestMethod]
    public void Acquire_WhileBusy_ThrowsAudiobookBusyException()
    {
        var gate = new AudiobookSaveGate();

        using var lease = gate.Acquire(BookId);

        var ex = Assert.ThrowsExactly<AudiobookBusyException>(() => gate.Acquire(BookId));
        Assert.AreEqual(BookId, ex.AudiobookId);
    }

    // Leases are disposed from `using` blocks and from a background task's finally, so a double
    // dispose is easy to introduce. It must not hand away a gate a *later* operation has taken.
    [TestMethod]
    public void DisposingALeaseTwice_DoesNotReleaseALaterOperationsHold()
    {
        var gate = new AudiobookSaveGate();

        gate.TryAcquire(BookId, out var first);
        first.Dispose();

        gate.TryAcquire(BookId, out var second);
        try
        {
            first.Dispose();

            Assert.IsTrue(gate.IsBusy(BookId), "the second operation still holds the gate");
            Assert.IsFalse(gate.TryAcquire(BookId, out _));
        }
        finally
        {
            second.Dispose();
        }
    }

    // The gate only excludes anything if every caller in the process shares one instance. It holds
    // its busy set on the instance (so unit tests are isolated from each other), which makes the
    // singleton registration the thing that actually provides the guarantee - and therefore the
    // thing worth guarding.
    [TestMethod]
    public void SetupServiceLayer_RegistersTheGateAsASingleton()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .Configure<AudiobookManagerSettings>(_ => { })
            .SetupServiceLayer();

        var descriptor = services.Single(d => d.ServiceType == typeof(IAudiobookSaveGate));
        Assert.AreEqual(ServiceLifetime.Singleton, descriptor.Lifetime);

        using var provider = services.BuildServiceProvider();
        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var fromA = scopeA.ServiceProvider.GetRequiredService<IAudiobookSaveGate>();
        var fromB = scopeB.ServiceProvider.GetRequiredService<IAudiobookSaveGate>();

        Assert.AreSame(fromA, fromB, "two request scopes must share one gate or it excludes nothing");

        using var lease = fromA.Acquire(BookId);
        Assert.IsTrue(fromB.IsBusy(BookId));
    }
}
