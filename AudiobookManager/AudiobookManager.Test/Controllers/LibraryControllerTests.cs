using System.Reflection;
using AudiobookManager.Api.Async;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Services;
using AudiobookManager.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AudiobookManager.Test.Controllers;

[TestClass]
public class LibraryControllerTests
{
    private Mock<IHubContext<OrganizeHub, IOrganize>> _hubContext = null!;
    private Mock<IServiceScopeFactory> _serviceScopeFactory = null!;
    private Mock<IOperationStatusRegistry> _statusRegistry = null!;
    private Mock<IDiscoveredAudiobookRepository> _discoveredRepo = null!;
    private Mock<ILibraryScanService> _libraryScanService = null!;
    private Mock<ILogger<LibraryController>> _logger = null!;
    private string _libraryPath = null!;
    private LibraryController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _hubContext = new Mock<IHubContext<OrganizeHub, IOrganize>>();
        _serviceScopeFactory = new Mock<IServiceScopeFactory>();
        _statusRegistry = new Mock<IOperationStatusRegistry>();
        _discoveredRepo = new Mock<IDiscoveredAudiobookRepository>();
        _libraryScanService = new Mock<ILibraryScanService>();
        _logger = new Mock<ILogger<LibraryController>>();

        // A real directory: StartLibraryScan refuses outright when the configured library path
        // is not there, so the default fixture has to look like a mounted library.
        _libraryPath = Path.Combine(Path.GetTempPath(), $"abm-library-ctl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_libraryPath);

        _controller = new LibraryController(
            _hubContext.Object,
            _serviceScopeFactory.Object,
            _statusRegistry.Object,
            _discoveredRepo.Object,
            _libraryScanService.Object,
            Mock.Of<IHostApplicationLifetime>(),
            Options.Create(new AudiobookManagerSettings { AudiobookLibraryPath = _libraryPath }),
            _logger.Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_libraryPath))
        {
            Directory.Delete(_libraryPath, recursive: true);
        }
    }

    private void SetupScope(ILibraryScanOrchestrator orchestrator)
    {
        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetService(typeof(ILibraryScanOrchestrator)))
            .Returns(orchestrator);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
        _serviceScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);
    }

    private static DiscoveredAudiobook MakeWellTagged(string fullPath) => new(
        "A Book", fullPath, Path.GetFileName(fullPath), 1000, DateTime.UtcNow)
    {
        Authors = "Author",
        Year = 2024
    };

    // Reflection guard, not a regression test for one specific field: DiscoveredAudiobookDto is a
    // hand-maintained subset of the DiscoveredAudiobook database model's properties, not derived
    // from it, so nothing stops a new column LibraryScanService starts populating from silently
    // never reaching the DTO - which is exactly what happened to Description/Copyright/Publisher/
    // Language/Rating/Asin/Www/DurationInSeconds (see
    // GetDiscovered_MapsDescriptionCopyrightAndOtherScannedFieldsOntoTheDto below). This fails the
    // moment a new property is added to the model without a same-named property on the DTO,
    // rather than relying on someone noticing the edit form is quietly showing a field blank.
    [TestMethod]
    public void DiscoveredAudiobookDto_CoversEveryPropertyOnTheDatabaseModel()
    {
        // Id is server-generated, never set by the scan. DiscoveredAt is scan bookkeeping, not
        // tag data. The three FileInfo* properties are represented under different DTO names
        // (FullPath/FileName/SizeInBytes) rather than omitted.
        var excludedFromDto = new HashSet<string>
        {
            nameof(DiscoveredAudiobook.Id),
            nameof(DiscoveredAudiobook.DiscoveredAt),
            nameof(DiscoveredAudiobook.FileInfoFullPath),
            nameof(DiscoveredAudiobook.FileInfoFileName),
            nameof(DiscoveredAudiobook.FileInfoSizeInBytes),
        };

        var modelPropertyNames = typeof(DiscoveredAudiobook)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(name => !excludedFromDto.Contains(name));

        var dtoPropertyNames = typeof(DiscoveredAudiobookDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        var missing = modelPropertyNames.Where(name => !dtoPropertyNames.Contains(name)).ToList();

        Assert.IsTrue(missing.Count == 0,
            "DiscoveredAudiobookDto is missing a property for these DiscoveredAudiobook database " +
            $"model fields: {string.Join(", ", missing)}. Add it to the DTO (and its constructor " +
            "mapping) so the discovered-books edit form doesn't silently show it blank.");
    }

    // Regression: DiscoveredAudiobookDto only mapped fullPath/fileName/sizeInBytes/bookName/
    // subtitle/series/seriesPart/year/authors/narrators/genres - Description, Copyright,
    // Publisher, Language, Rating, Asin, Www and DurationInSeconds are all stored on the scan
    // (LibraryScanService copies them from the parsed tags), but the DTO silently dropped every
    // one of them, so the edit form for every discovered book always showed those fields empty
    // regardless of what the file actually had tagged - indistinguishable in the UI from the file
    // genuinely having no description, and irreversible if organized: DiscoveredAudiobooks.tsx's
    // initialAudiobook is built entirely from this DTO, so the "empty" description a user never
    // touched would be saved as empty, silently erasing a real one on organize.
    [TestMethod]
    public async Task GetDiscovered_MapsDescriptionCopyrightAndOtherScannedFieldsOntoTheDto()
    {
        var entry = MakeWellTagged("/import/book.m4b");
        entry.Description = "A real description read from the file at scan time";
        entry.Copyright = "2021 Andy Weir";
        entry.Publisher = "Podium Audio";
        entry.Language = "en";
        entry.Rating = "4.5";
        entry.Asin = "B08G9PRS1K";
        entry.Www = "https://example.com";
        entry.DurationInSeconds = 58248;

        _discoveredRepo.Setup(r => r.GetPaginatedAsync(20, 0, null))
            .ReturnsAsync((new List<DiscoveredAudiobook> { entry }, 1));
        _libraryScanService.Setup(s => s.IsDuplicateTarget(entry)).Returns(false);

        var result = await _controller.GetDiscovered();

        var dto = result.Items[0];
        Assert.AreEqual(entry.Description, dto.Description);
        Assert.AreEqual(entry.Copyright, dto.Copyright);
        Assert.AreEqual(entry.Publisher, dto.Publisher);
        Assert.AreEqual(entry.Language, dto.Language);
        Assert.AreEqual(entry.Rating, dto.Rating);
        Assert.AreEqual(entry.Asin, dto.Asin);
        Assert.AreEqual(entry.Www, dto.Www);
        Assert.AreEqual(entry.DurationInSeconds, dto.DurationInSeconds);
    }

    [TestMethod]
    public async Task GetDiscovered_WellTaggedEntry_IsFlaggedDuplicateWhenTargetPathIsOccupied()
    {
        var entry = MakeWellTagged("/import/book.m4b");
        _discoveredRepo.Setup(r => r.GetPaginatedAsync(20, 0, null))
            .ReturnsAsync((new List<DiscoveredAudiobook> { entry }, 1));
        _libraryScanService.Setup(s => s.IsDuplicateTarget(entry)).Returns(true);

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
        _libraryScanService.Setup(s => s.IsDuplicateTarget(entry)).Returns(false);

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
        _libraryScanService.Verify(s => s.IsDuplicateTarget(It.IsAny<DiscoveredAudiobook>()), Times.Never);
    }

    [TestMethod]
    public async Task GetDiscovered_MultipleWellTaggedEntries_EachGetsItsOwnDuplicateResultRegardlessOfParallelChecks()
    {
        // The page's duplicate checks run in parallel, so each item's own result must still land
        // on the correct dto rather than getting crossed with another item's.
        var duplicateEntry = MakeWellTagged("/import/dup.m4b");
        var freeEntry = MakeWellTagged("/import/free.m4b");
        var notWellTagged = new DiscoveredAudiobook("Untagged", "/import/untagged.m4b", "untagged.m4b", 1000, DateTime.UtcNow)
        {
            Authors = null,
            Year = null
        };

        _discoveredRepo.Setup(r => r.GetPaginatedAsync(20, 0, null))
            .ReturnsAsync((new List<DiscoveredAudiobook> { duplicateEntry, freeEntry, notWellTagged }, 3));
        _libraryScanService.Setup(s => s.IsDuplicateTarget(duplicateEntry)).Returns(true);
        _libraryScanService.Setup(s => s.IsDuplicateTarget(freeEntry)).Returns(false);

        var result = await _controller.GetDiscovered();

        Assert.AreEqual(3, result.Items.Count);
        Assert.IsTrue(result.Items.Single(i => i.FullPath == "/import/dup.m4b").IsDuplicate);
        Assert.IsFalse(result.Items.Single(i => i.FullPath == "/import/free.m4b").IsDuplicate);
        Assert.IsFalse(result.Items.Single(i => i.FullPath == "/import/untagged.m4b").IsDuplicate);
        _libraryScanService.Verify(s => s.IsDuplicateTarget(notWellTagged), Times.Never);
    }

    [TestMethod]
    public async Task GetDiscovered_IncludesWellTaggedTotalFromRepository()
    {
        _discoveredRepo.Setup(r => r.GetPaginatedAsync(20, 0, null))
            .ReturnsAsync((new List<DiscoveredAudiobook>(), 4));
        _discoveredRepo.Setup(r => r.CountWellTaggedAsync()).ReturnsAsync(3);

        var result = await _controller.GetDiscovered();

        Assert.AreEqual(4, result.Total);
        Assert.AreEqual(3, result.WellTaggedTotal);
    }

    [TestMethod]
    public void StartBulkImportWellTagged_StartsBackgroundOperation()
    {
        var result = _controller.StartBulkImportWellTagged();

        Assert.IsNotNull(result);
    }

    [TestMethod]
    public async Task GetDiscovered_WithSearch_DoesNotCountAllWellTaggedRows()
    {
        _discoveredRepo.Setup(r => r.GetPaginatedAsync(20, 0, "author"))
            .ReturnsAsync((new List<DiscoveredAudiobook>(), 0));

        var result = await _controller.GetDiscovered(search: "author");

        Assert.AreEqual(0, result.WellTaggedTotal);
        _discoveredRepo.Verify(r => r.CountWellTaggedAsync(), Times.Never);
    }

    #region Combined scan

    [TestMethod]
    public void StartLibraryScan_LibraryDirectoryMissing_Returns409WithActionableDetail()
    {
        // BackgroundOperationRunner is fire-and-forget, so an exception thrown inside the work
        // reaches the client as zeroed completion events - which reads as "your library is
        // fine". Refusing here is what makes a missing mount legible.
        var controller = new LibraryController(
            _hubContext.Object,
            _serviceScopeFactory.Object,
            _statusRegistry.Object,
            _discoveredRepo.Object,
            _libraryScanService.Object,
            Mock.Of<IHostApplicationLifetime>(),
            Options.Create(new AudiobookManagerSettings
            {
                AudiobookLibraryPath = Path.Combine(Path.GetTempPath(), $"abm-not-mounted-{Guid.NewGuid():N}")
            }),
            _logger.Object);

        var result = (ObjectResult)controller.StartLibraryScan();
        var problem = (ProblemDetails)result.Value!;

        Assert.AreEqual(StatusCodes.Status409Conflict, result.StatusCode);
        // The message has to travel in `detail`: the client reads ApiError.message from there,
        // and it must agree with the orchestrator's own refusal (they share the wording via
        // LibraryAvailability so a legible refusal up front is the same text as the guard).
        StringAssert.Contains(problem.Detail, "is not available");
        StringAssert.Contains(problem.Detail, "volume mount");
        _serviceScopeFactory.Verify(f => f.CreateScope(), Times.Never);
    }

    // The combined run is the library scan now: resolving the orchestrator from a fresh scope
    // (never the controller's request scope), progress through both event families, and two
    // completion events - one per half of the operation.
    [TestMethod]
    public async Task StartLibraryScan_InvokesTheOrchestrator_AndReportsBothProgressAndCompletion()
    {
        var clientProxy = new Mock<IOrganize>();
        var clients = new Mock<IHubClients<IOrganize>>();
        clients.Setup(c => c.All).Returns(clientProxy.Object);
        _hubContext.Setup(h => h.Clients).Returns(clients.Object);

        var orchestrator = new Mock<ILibraryScanOrchestrator>();
        orchestrator.Setup(o => o.RunCombinedScanAsync(
                It.IsAny<Func<string, int, int, Task>>(),
                It.IsAny<Func<string, int, int, int, Task>>(),
                It.IsAny<Func<int, int, int, Task>?>()))
            .ReturnsAsync((Func<string, int, int, Task> discovery,
                Func<string, int, int, int, Task> consistency,
                Func<int, int, int, Task>? discoveryCompleted) =>
            {
                discovery("Discovered: book.m4b", 1, 1).GetAwaiter().GetResult();
                // The real orchestrator reports the scan's completion as soon as discovery
                // finishes, before the consistency half runs.
                if (discoveryCompleted != null)
                {
                    discoveryCompleted(1, 1, 0).GetAwaiter().GetResult();
                }
                consistency("Checked: A Book", 1, 1, 3).GetAwaiter().GetResult();
                return new CombinedScanResult(1, 1, 0, 1, 3);
            });
        SetupScope(orchestrator.Object);

        var result = _controller.StartLibraryScan();

        Assert.IsInstanceOfType<OkResult>(result);

        await OperationGate.WaitUntilReleasedAsync(typeof(LibraryController));

        orchestrator.Verify(o => o.RunCombinedScanAsync(
            It.IsAny<Func<string, int, int, Task>>(),
            It.IsAny<Func<string, int, int, int, Task>>(),
            It.IsAny<Func<int, int, int, Task>?>()), Times.Once);
        clientProxy.Verify(c => c.LibraryScanProgress(It.Is<LibraryScanProgress>(p =>
            p.Message == "Discovered: book.m4b" && p.FilesScanned == 1 && p.TotalFiles == 1)), Times.Once);
        clientProxy.Verify(c => c.LibraryScanComplete(It.Is<LibraryScanComplete>(r =>
            r.TotalFilesScanned == 1 && r.NewFilesDiscovered == 1 && r.AlreadyTracked == 0)), Times.Once);
        clientProxy.Verify(c => c.ConsistencyCheckProgress(It.Is<ConsistencyCheckProgress>(p =>
            p.Message == "Checked: A Book" && p.BooksChecked == 1 && p.TotalBooks == 1
                && p.IssuesFound == 3 && p.Scope == ConsistencyCheckScope.Library)), Times.Once);
        clientProxy.Verify(c => c.ConsistencyCheckComplete(It.Is<ConsistencyCheckComplete>(r =>
            r.TotalBooksChecked == 1 && r.TotalIssuesFound == 3 && r.Scope == ConsistencyCheckScope.Library)), Times.Once);
    }

    [TestMethod]
    public async Task StartLibraryScan_SecondRunWhileTheFirstIsHeld_Returns409()
    {
        // The scan gate is process-static. The first run's work blocks on a completion signal
        // rather than Delay(Infinite): a forever-held gate would poison every scan/check test
        // ordered after this one in the run.
        var workMayFinish = new TaskCompletionSource();
        var orchestrator = new Mock<ILibraryScanOrchestrator>();
        orchestrator.Setup(o => o.RunCombinedScanAsync(
                It.IsAny<Func<string, int, int, Task>>(),
                It.IsAny<Func<string, int, int, int, Task>>(),
                It.IsAny<Func<int, int, int, Task>?>()))
            .Returns(async () =>
            {
                await workMayFinish.Task;
                return new CombinedScanResult(0, 0, 0, 0, 0);
            });
        SetupScope(orchestrator.Object);

        var first = _controller.StartLibraryScan();
        try
        {
            Assert.IsInstanceOfType<OkResult>(first);

            var second = (ObjectResult)_controller.StartLibraryScan();
            Assert.AreEqual(StatusCodes.Status409Conflict, second.StatusCode);
            orchestrator.Verify(o => o.RunCombinedScanAsync(
                It.IsAny<Func<string, int, int, Task>>(),
                It.IsAny<Func<string, int, int, int, Task>>(),
                It.IsAny<Func<int, int, int, Task>?>()), Times.Once);
        }
        finally
        {
            // Let the first operation finish so the gate is free again for later tests, and wait
            // on the real release rather than a fixed sleep (the gate is process-static). Run in
            // finally so a failed assertion above can't leak the shared gate into later tests.
            workMayFinish.SetResult();
            await OperationGate.WaitUntilReleasedAsync(typeof(LibraryController));
        }
    }

    [TestMethod]
    public async Task StartLibraryScan_Error_EmitsBothZeroedCompletionEvents()
    {
        var clientProxy = new Mock<IOrganize>();
        var clients = new Mock<IHubClients<IOrganize>>();
        clients.Setup(c => c.All).Returns(clientProxy.Object);
        _hubContext.Setup(h => h.Clients).Returns(clients.Object);

        var orchestrator = new Mock<ILibraryScanOrchestrator>();
        orchestrator.Setup(o => o.RunCombinedScanAsync(
                It.IsAny<Func<string, int, int, Task>>(),
                It.IsAny<Func<string, int, int, int, Task>>(),
                It.IsAny<Func<int, int, int, Task>?>()))
            .ThrowsAsync(new Exception("boom"));
        SetupScope(orchestrator.Object);

        var result = _controller.StartLibraryScan();

        Assert.IsInstanceOfType<OkResult>(result);

        await OperationGate.WaitUntilReleasedAsync(typeof(LibraryController));

        // A failure in one half must not leave the client waiting on the other: both families
        // complete, zeroed, so neither progress bar is left spinning.
        clientProxy.Verify(c => c.LibraryScanComplete(It.Is<LibraryScanComplete>(r =>
            r.TotalFilesScanned == 0 && r.NewFilesDiscovered == 0 && r.AlreadyTracked == 0)), Times.Once);
        clientProxy.Verify(c => c.ConsistencyCheckComplete(It.Is<ConsistencyCheckComplete>(r =>
            r.TotalBooksChecked == 0 && r.TotalIssuesFound == 0 && r.Scope == ConsistencyCheckScope.Library)), Times.Once);
    }

    // Regression: the scan completion used to fire only after the whole combined run returned,
    // so if discovery committed rows and then the consistency half threw, the runner's error path
    // sent LibraryScanComplete(0,0,0) - telling the user "0 new files" for rows that were
    // committed and are visible on the Discovered page. The completion now fires at the end of
    // discovery; on error it must not be clobbered by the zeroed send.
    [TestMethod]
    public async Task StartLibraryScan_ErrorAfterDiscovery_KeepsTheRealScanCompletionAndZeroesOnlyConsistency()
    {
        var clientProxy = new Mock<IOrganize>();
        var clients = new Mock<IHubClients<IOrganize>>();
        clients.Setup(c => c.All).Returns(clientProxy.Object);
        _hubContext.Setup(h => h.Clients).Returns(clients.Object);

        var orchestrator = new Mock<ILibraryScanOrchestrator>();
        orchestrator.Setup(o => o.RunCombinedScanAsync(
                It.IsAny<Func<string, int, int, Task>>(),
                It.IsAny<Func<string, int, int, int, Task>>(),
                It.IsAny<Func<int, int, int, Task>?>()))
            .Returns(async (Func<string, int, int, Task> discovery,
                Func<string, int, int, int, Task> consistency,
                Func<int, int, int, Task>? discoveryCompleted) =>
            {
                discovery("Discovered: new.m4b", 1, 1).GetAwaiter().GetResult();
                if (discoveryCompleted != null)
                {
                    discoveryCompleted(1, 1, 0).GetAwaiter().GetResult();
                }
                throw new Exception("consistency boom");
            });
        SetupScope(orchestrator.Object);

        var result = _controller.StartLibraryScan();

        Assert.IsInstanceOfType<OkResult>(result);

        await OperationGate.WaitUntilReleasedAsync(typeof(LibraryController));

        // The scan completion reported the real counts and was sent exactly once - the error
        // path must not overwrite it with a zeroed one.
        clientProxy.Verify(c => c.LibraryScanComplete(It.Is<LibraryScanComplete>(r =>
            r.TotalFilesScanned == 1 && r.NewFilesDiscovered == 1 && r.AlreadyTracked == 0)), Times.Once);
        clientProxy.Verify(c => c.LibraryScanComplete(It.Is<LibraryScanComplete>(r =>
            r.TotalFilesScanned == 0 && r.NewFilesDiscovered == 0 && r.AlreadyTracked == 0)), Times.Never);
        // The consistency half never reported its own completion, so zeroed-on-error stands (the
        // pre-existing behavior for the event that never fired).
        clientProxy.Verify(c => c.ConsistencyCheckComplete(It.Is<ConsistencyCheckComplete>(r =>
            r.TotalBooksChecked == 0 && r.TotalIssuesFound == 0 && r.Scope == ConsistencyCheckScope.Library)), Times.Once);
    }

    #endregion

}
