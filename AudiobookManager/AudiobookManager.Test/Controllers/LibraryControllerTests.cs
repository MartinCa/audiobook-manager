using AudiobookManager.Api.Async;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.Controllers;

[TestClass]
public class LibraryControllerTests
{
    private Mock<IDiscoveredAudiobookRepository> _discoveredRepo = null!;
    private Mock<ILibraryScanService> _libraryScanService = null!;
    private LibraryController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _discoveredRepo = new Mock<IDiscoveredAudiobookRepository>();
        _libraryScanService = new Mock<ILibraryScanService>();

        _controller = new LibraryController(
            new Mock<IHubContext<OrganizeHub, IOrganize>>().Object,
            new Mock<IServiceScopeFactory>().Object,
            new Mock<IOperationStatusRegistry>().Object,
            _discoveredRepo.Object,
            _libraryScanService.Object,
            Mock.Of<IHostApplicationLifetime>(),
            new Mock<ILogger<LibraryController>>().Object);
    }

    private static DiscoveredAudiobook MakeWellTagged(string fullPath) => new(
        "A Book", fullPath, Path.GetFileName(fullPath), 1000, DateTime.UtcNow)
    {
        Authors = "Author",
        Year = 2024
    };

    [TestMethod]
    public async Task GetDiscovered_WellTaggedEntry_IsFlaggedDuplicateWhenTargetPathIsOccupied()
    {
        var entry = MakeWellTagged("/import/book.m4b");
        _discoveredRepo.Setup(r => r.GetPaginatedAsync(20, 0, null))
            .ReturnsAsync((new List<DiscoveredAudiobook> { entry }, 1));
        _libraryScanService.Setup(s => s.IsDuplicateTargetAsync(entry)).ReturnsAsync(true);

        var result = await _controller.GetDiscovered();

        Assert.AreEqual(1, result.Items.Count);
        Assert.IsTrue(result.Items[0].IsDuplicate);
    }

    [TestMethod]
    public async Task GetDiscovered_WellTaggedEntry_IsNotFlaggedDuplicateWhenTargetPathIsFree()
    {
        var entry = MakeWellTagged("/import/book.m4b");
        _discoveredRepo.Setup(r => r.GetPaginatedAsync(20, 0, null))
            .ReturnsAsync((new List<DiscoveredAudiobook> { entry }, 1));
        _libraryScanService.Setup(s => s.IsDuplicateTargetAsync(entry)).ReturnsAsync(false);

        var result = await _controller.GetDiscovered();

        Assert.IsFalse(result.Items[0].IsDuplicate);
    }

    [TestMethod]
    public async Task GetDiscovered_NotWellTaggedEntry_SkipsTheDuplicateCheckEntirely()
    {
        var entry = new DiscoveredAudiobook("A Book", "/import/book.m4b", "book.m4b", 1000, DateTime.UtcNow)
        {
            Authors = null,
            Year = null
        };
        _discoveredRepo.Setup(r => r.GetPaginatedAsync(20, 0, null))
            .ReturnsAsync((new List<DiscoveredAudiobook> { entry }, 1));

        var result = await _controller.GetDiscovered();

        Assert.IsFalse(result.Items[0].IsWellTagged);
        Assert.IsFalse(result.Items[0].IsDuplicate);
        _libraryScanService.Verify(s => s.IsDuplicateTargetAsync(It.IsAny<DiscoveredAudiobook>()), Times.Never);
    }

    [TestMethod]
    public async Task GetDiscovered_MultipleWellTaggedEntries_EachGetsItsOwnDuplicateResultRegardlessOfConcurrentChecks()
    {
        // Duplicate checks run concurrently now (Task.WhenAll), so each item's own result must
        // still land on the correct dto rather than getting crossed with another item's.
        var duplicateEntry = MakeWellTagged("/import/dup.m4b");
        var freeEntry = MakeWellTagged("/import/free.m4b");
        var notWellTagged = new DiscoveredAudiobook("Untagged", "/import/untagged.m4b", "untagged.m4b", 1000, DateTime.UtcNow)
        {
            Authors = null,
            Year = null
        };

        _discoveredRepo.Setup(r => r.GetPaginatedAsync(20, 0, null))
            .ReturnsAsync((new List<DiscoveredAudiobook> { duplicateEntry, freeEntry, notWellTagged }, 3));
        _libraryScanService.Setup(s => s.IsDuplicateTargetAsync(duplicateEntry)).ReturnsAsync(true);
        _libraryScanService.Setup(s => s.IsDuplicateTargetAsync(freeEntry)).ReturnsAsync(false);

        var result = await _controller.GetDiscovered();

        Assert.AreEqual(3, result.Items.Count);
        Assert.IsTrue(result.Items.Single(i => i.FullPath == "/import/dup.m4b").IsDuplicate);
        Assert.IsFalse(result.Items.Single(i => i.FullPath == "/import/free.m4b").IsDuplicate);
        Assert.IsFalse(result.Items.Single(i => i.FullPath == "/import/untagged.m4b").IsDuplicate);
        _libraryScanService.Verify(s => s.IsDuplicateTargetAsync(notWellTagged), Times.Never);
    }
}
