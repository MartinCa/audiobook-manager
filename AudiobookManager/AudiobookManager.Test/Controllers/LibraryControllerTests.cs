using System.Reflection;
using AudiobookManager.Api.Async;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
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
}
