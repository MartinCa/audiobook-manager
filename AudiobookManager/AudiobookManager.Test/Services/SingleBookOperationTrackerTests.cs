using AudiobookManager.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookManager.Test.Services;

[TestClass]
public class SingleBookOperationTrackerTests
{
    private const string Select = "select";
    private const string Apply = "apply";

    [TestMethod]
    public void TryBegin_WhileTheSameOperationRunsForTheSameBook_Fails()
    {
        var tracker = new SingleBookOperationTracker();

        Assert.IsTrue(tracker.TryBegin(Select, 1, out var first));
        using (first)
        {
            Assert.IsFalse(tracker.TryBegin(Select, 1, out _));
            Assert.IsTrue(tracker.IsRunning(Select, 1));
        }
    }

    [TestMethod]
    public void TryBegin_ADifferentBookOrADifferentOperation_IsIndependent()
    {
        var tracker = new SingleBookOperationTracker();

        Assert.IsTrue(tracker.TryBegin(Select, 1, out var first));
        using (first)
        {
            Assert.IsTrue(tracker.TryBegin(Select, 2, out var otherBook));
            otherBook.Dispose();
            Assert.IsTrue(tracker.TryBegin(Apply, 1, out var otherOperation));
            otherOperation.Dispose();
        }
    }

    [TestMethod]
    public void Dispose_ReleasesTheSlot_AndIsIdempotent()
    {
        var tracker = new SingleBookOperationTracker();
        Assert.IsTrue(tracker.TryBegin(Select, 1, out var first));
        first.Dispose();
        Assert.IsFalse(tracker.IsRunning(Select, 1));

        Assert.IsTrue(tracker.TryBegin(Select, 1, out var second));
        using (second)
        {
            // A stale lease disposed again must not free the slot a later operation now holds.
            first.Dispose();
            Assert.IsTrue(tracker.IsRunning(Select, 1));
        }
    }

    [TestMethod]
    public void RefusedAttempt_DoesNotReleaseTheHoldersSlotWhenItsLeaseIsDisposed()
    {
        var tracker = new SingleBookOperationTracker();
        Assert.IsTrue(tracker.TryBegin(Select, 1, out var holder));
        using (holder)
        {
            Assert.IsFalse(tracker.TryBegin(Select, 1, out var refused));
            refused.Dispose();

            Assert.IsTrue(tracker.IsRunning(Select, 1));
        }
    }

    // The tracker is only an exclusion if every request shares one instance.
    [TestMethod]
    public void ServiceLayer_RegistersItAsASingleton()
    {
        var services = new ServiceCollection();
        services.SetupServiceLayer();

        var descriptor = services.Single(d => d.ServiceType == typeof(ISingleBookOperationTracker));

        Assert.AreEqual(ServiceLifetime.Singleton, descriptor.Lifetime);
    }
}
