using System.Data.Common;
using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Scraping;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Scraping;

/// <summary>
/// Runs against a real (temp-file) SQLite database rather than a mock, because what is under test
/// is how many times the mappings are actually read from the context and whether concurrent
/// callers can overlap on it.
/// </summary>
[TestClass]
public class BookSeriesMapperTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private CountingCommandInterceptor _interceptor = null!;

    /// <summary>Counts the SELECTs actually issued against series_mapping.</summary>
    private class CountingCommandInterceptor : DbCommandInterceptor
    {
        private int _count;

        public int SeriesMappingReads => _count;

        public void Reset() => Interlocked.Exchange(ref _count, 0);

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Count(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count(command);
            return ValueTask.FromResult(result);
        }

        private void Count(DbCommand command)
        {
            if (command.CommandText.Contains("series_mapping", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _count);
            }
        }
    }

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"seriesmapper-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _interceptor = new CountingCommandInterceptor();

        var options = new DbContextOptionsBuilder<DatabaseContext>().AddInterceptors(_interceptor).Options;
        _db = new DatabaseContext(options, settings);
        _db.Database.EnsureCreated();

        // Mappings are owned by a Series row now: the target is always the owner's name, so the
        // seed must create the owning rows and point the patterns at them.
        _db.Series.AddRange(
            new Series { Name = "The Stormlight Archive" },
            new Series { Name = "Mistborn" });
        _db.SaveChanges();

        var stormlight = _db.Series.Single(s => s.Name == "The Stormlight Archive");
        var mistborn = _db.Series.Single(s => s.Name == "Mistborn");
        _db.SeriesMappings.AddRange(
            new SeriesMapping(default, "^Stormlight.*", false, stormlight.Id),
            new SeriesMapping(default, "^Mistborn.*", false, mistborn.Id));
        _db.SaveChanges();
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

    /// <summary>
    /// Resets the read counter as well, so seeding (whose INSERTs go through the same reader
    /// interception path, since SQLite returns generated keys with RETURNING) is not counted
    /// against the mapper.
    /// </summary>
    private BookSeriesMapper CreateMapper()
    {
        _interceptor.Reset();
        return new BookSeriesMapper(_db, NullLogger<BookSeriesMapper>.Instance);
    }

    private static IList<MetadataSeriesSearchResult> Results(params string[] names) =>
        names.Select(n => new MetadataSeriesSearchResult(n)).ToList();

    [TestMethod]
    public async Task MapBookSeries_RepeatedCalls_ReadTheMappingsOnce()
    {
        // Every scraped result runs through here, so this used to be one SELECT per result -
        // twenty identical queries for a single Audible search.
        var mapper = CreateMapper();

        await mapper.MapBookSeries(Results("Stormlight Archive"));
        await mapper.MapBookSeries(Results("Mistborn Series"));
        await mapper.MapBookSeries(Results("Something Else"));

        Assert.AreEqual(1, _interceptor.SeriesMappingReads);
    }

    [TestMethod]
    public async Task MapSingleBookSeries_ConcurrentCallers_ReadTheMappingsOnce()
    {
        // The shape the scrapers actually use: AudibleScraper.Search fans out one task per hit,
        // and ScrapingService.SearchMultiple runs three scrapers that share this scoped instance.
        // Each of those used to start its own query against a DbContext that permits one
        // operation at a time.
        var mapper = CreateMapper();

        var tasks = Enumerable.Range(0, 20)
            .Select(i => mapper.MapSingleBookSeries(new MetadataSeriesSearchResult($"Stormlight {i}")))
            .ToList();

        var mapped = await Task.WhenAll(tasks);

        Assert.AreEqual(20, mapped.Length);
        Assert.AreEqual(1, _interceptor.SeriesMappingReads);
    }

    [TestMethod]
    public async Task MapBookSeries_AppliesTheMapping_AfterCaching()
    {
        // Caching must not change what the mapping does.
        var mapper = CreateMapper();

        var first = await mapper.MapBookSeries(Results("Stormlight Archive"));
        var second = await mapper.MapBookSeries(Results("Stormlight Archive"));

        Assert.AreEqual("The Stormlight Archive", first.Single().SeriesName);
        Assert.AreEqual("The Stormlight Archive", second.Single().SeriesName);
        Assert.AreEqual("Stormlight Archive", second.Single().OriginalSeriesName);
    }

    [TestMethod]
    public async Task MapBookSeries_TargetIsTheOwningSeriesName_NotAValueOnTheMapping()
    {
        // The mapping pattern has no target of its own: renaming the owning series row must
        // change what the pattern maps to. (This is the data-model ownership the old global
        // mapped_series column approximated.)
        var owner = _db.Series.Single(s => s.Name == "The Stormlight Archive");
        owner.Name = "The Stormlight Archive (renamed)";
        _db.SaveChanges();

        var mapper = CreateMapper();

        var mapped = await mapper.MapBookSeries(Results("Stormlight Archive"));

        Assert.AreEqual("The Stormlight Archive (renamed)", mapped.Single().SeriesName);
    }

    [TestMethod]
    public async Task MapSingleBookSeries_StillLoadsTheMappingsWithTheirOwnerInOneRead()
    {
        // The Include that joins the owner's name must not turn the single-select guarantee into
        // a per-result N+1: MapSingleBookSeries fanned out 20 ways must still hit the table once.
        var mapper = CreateMapper();

        var tasks = Enumerable.Range(0, 20)
            .Select(i => mapper.MapSingleBookSeries(new MetadataSeriesSearchResult($"Mistborn {i}")))
            .ToList();

        var mapped = await Task.WhenAll(tasks);

        Assert.AreEqual("Mistborn", mapped[0].SeriesName);
        Assert.AreEqual(1, _interceptor.SeriesMappingReads);
    }

    [TestMethod]
    public async Task MapBookSeriesPerBook_PreservesPerBookGrouping()
    {
        // The contract the grouped overload exists for: group i out is group i in, entry for
        // entry - a caller can never slice one book's mapped series onto another book's result.
        var mapper = CreateMapper();

        var books = new List<IList<MetadataSeriesSearchResult>>
        {
            Results("Stormlight Archive", "Something Else"),
            new List<MetadataSeriesSearchResult>(), // a book with no series must stay an empty group, not shift the grouping
            Results("Mistborn Series", "Unmatched Series"),
        };

        var mapped = await mapper.MapBookSeriesPerBook(books);

        Assert.AreEqual(3, mapped.Count);
        Assert.AreEqual(2, mapped[0].Count);
        Assert.AreEqual("The Stormlight Archive", mapped[0][0].SeriesName);
        Assert.AreEqual("Something Else", mapped[0][1].SeriesName);
        Assert.AreEqual(0, mapped[1].Count);
        Assert.AreEqual(2, mapped[2].Count);
        Assert.AreEqual("Mistborn", mapped[2][0].SeriesName);
        Assert.AreEqual("Unmatched", mapped[2][1].SeriesName);
    }

    [TestMethod]
    public async Task MapBookSeries_UnmatchedName_IsReturnedCleanedNotMapped()
    {
        var mapper = CreateMapper();

        var mapped = await mapper.MapBookSeries(Results("Wheel of Time Series"));

        // "Series" is stripped by CleanSeriesName; no mapping row matches, so nothing is rewritten.
        Assert.AreEqual("Wheel of Time", mapped.Single().SeriesName);
    }

    [TestMethod]
    public async Task MapBookSeries_InvalidPatternRow_IsSkippedNotThrown()
    {
        // A user-supplied pattern that does not compile must not take the whole result set down;
        // caching the compiled list must not change that. The mapping still needs an owning row.
        var brokenOwner = new Series { Name = "Broken" };
        _db.Series.Add(brokenOwner);
        _db.SaveChanges();
        _db.SeriesMappings.Add(new SeriesMapping(default, "([unclosed", false, brokenOwner.Id));
        await _db.SaveChangesAsync();

        var mapper = CreateMapper();

        var mapped = await mapper.MapBookSeries(Results("Stormlight Archive"));

        Assert.AreEqual("The Stormlight Archive", mapped.Single().SeriesName);
    }

    [TestMethod]
    public void BookSeriesMapper_IsRegisteredScoped()
    {
        // "Loaded once per scope" is only true because every scraper in a request shares one
        // mapper instance. Registered transient instead, each of the three scrapers would get its
        // own cache and the per-result query count would partly come back - silently, since
        // nothing else would fail. Asserted on the descriptor rather than by resolving, which
        // would need the whole database graph stood up to prove a registration detail.
        var services = new ServiceCollection().SetupScraping();

        var descriptor = services.Single(d => d.ServiceType == typeof(IBookSeriesMapper));

        Assert.AreEqual(ServiceLifetime.Scoped, descriptor.Lifetime);
    }
}
