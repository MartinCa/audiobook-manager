using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Scraping;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AudiobookManager.Test.Scraping;

[TestClass]
public class BookSeriesMapperTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private BookSeriesMapper _mapper = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"seriesmapper-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _mapper = new BookSeriesMapper(_db, new Mock<ILogger<BookSeriesMapper>>().Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private async Task SeedMappingAsync(string regex, string mappedSeries, bool warnAboutPart = false)
    {
        _db.SeriesMappings.Add(new SeriesMapping(default, regex, mappedSeries, warnAboutPart));
        await _db.SaveChangesAsync();
    }

    [TestMethod]
    public async Task MapBookSeries_AppliesAMatchingMappingAndStripsATrailingSeriesWord()
    {
        await SeedMappingAsync("^The Stormlight Archive$", "Stormlight");

        var results = await _mapper.MapBookSeries(new List<MetadataSeriesSearchResult>
        {
            new("The Stormlight Archive Series") { SeriesPart = "1" },
            new("Mistborn Series") { SeriesPart = "2" },
        });

        CollectionAssert.AreEqual(
            new[] { "Stormlight", "Mistborn" },
            results.Select(r => r.SeriesName).ToArray());
        CollectionAssert.AreEqual(
            new[] { "1", "2" },
            results.Select(r => r.SeriesPart).ToArray());
    }

    // Behavioral guard, not a proven-failing regression test: the duplicate work the fix removed
    // (a lazy `Select` of mapping tasks that was awaited and then enumerated a second time to
    // read .Result, starting a whole second set of mappings) produced identical values, so it
    // was invisible from the outside. This pins the contract the rewrite has to keep.
    [TestMethod]
    public async Task MapBookSeries_ReturnsOneMappedResultPerInputInOrder()
    {
        await SeedMappingAsync("^Foundation$", "Foundation Saga");

        var input = Enumerable.Range(0, 5)
            .Select(i => new MetadataSeriesSearchResult(i == 0 ? "Foundation" : $"Other {i}"))
            .ToList();

        var results = await _mapper.MapBookSeries(input);

        CollectionAssert.AreEqual(
            new[] { "Foundation Saga", "Other 1", "Other 2", "Other 3", "Other 4" },
            results.Select(r => r.SeriesName).ToArray());
    }

    // Regression: an invalid user-entered mapping pattern threw out of Regex's constructor, and
    // since every scraped result runs through this, one bad row turned every metadata search
    // into a failure.
    [TestMethod]
    public async Task MapBookSeries_InvalidMappingRegex_IsSkippedRatherThanFailingTheWholeBatch()
    {
        await SeedMappingAsync("[unclosed", "Never Applied");
        await SeedMappingAsync("^Dune$", "Dune Chronicles");

        var results = await _mapper.MapBookSeries(new List<MetadataSeriesSearchResult>
        {
            new("Dune"),
        });

        CollectionAssert.AreEqual(new[] { "Dune Chronicles" }, results.Select(r => r.SeriesName).ToArray());
    }
}
