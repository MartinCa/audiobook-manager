using System.Data.Common;
using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Scraping;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Settings;
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

        _db.SeriesMappings.Add(new SeriesMapping(default, "^Stormlight.*", "The Stormlight Archive", false));
        _db.SeriesMappings.Add(new SeriesMapping(default, "^Mistborn.*", "Mistborn", false));
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
        // caching the compiled list must not change that.
        _db.SeriesMappings.Add(new SeriesMapping(default, "([unclosed", "Broken", false));
        await _db.SaveChangesAsync();

        var mapper = CreateMapper();

        var mapped = await mapper.MapBookSeries(Results("Stormlight Archive"));

        Assert.AreEqual("The Stormlight Archive", mapped.Single().SeriesName);
    }
}
