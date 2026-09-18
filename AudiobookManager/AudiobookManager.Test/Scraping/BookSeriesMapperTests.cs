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

    /// <summary>
    /// A pattern that compiles but backtracks catastrophically must cost its own mapping and
    /// nothing else. Before the match timeout this did not "fail" - it never returned: every
    /// scraped result runs through the mapping scan, so one such row wedged the request thread
    /// and with it every metadata search from then on. The test can only exist because the match
    /// is now bounded; against the untimed code it hangs rather than going red.
    ///
    /// No fixed wait is involved: the assertion is on the real outcome (the other mapping still
    /// applied), and the elapsed check only states that the scan returned in a bounded time
    /// rather than running to completion, which for this input would take longer than the suite.
    /// </summary>
    [TestMethod]
    public async Task MapBookSeries_CatastrophicallyBacktrackingPattern_IsSkippedNotHung()
    {
        var evilOwner = new Series { Name = "Evil" };
        _db.Series.Add(evilOwner);
        _db.SaveChanges();
        // The classic nested-quantifier blowup. Ordered before the Stormlight row by id, so the
        // scan reaches it first and has to survive it to get to the mapping that does apply.
        _db.SeriesMappings.Add(new SeriesMapping(default, "^(a+)+$", false, evilOwner.Id));
        await _db.SaveChangesAsync();

        var mapper = CreateMapper();
        // Every prefix matches (a+)+ and the trailing "!" defeats the anchor, so the engine walks
        // every partition of the a-run before giving up - exponential in the run's length.
        var pathological = new string('a', 40) + "!";

        var started = System.Diagnostics.Stopwatch.StartNew();
        var mapped = await mapper.MapBookSeries(Results(pathological, "Stormlight Archive"));
        started.Stop();

        // The runaway row does not match, so its input is passed through untouched...
        Assert.AreEqual(pathological, mapped[0].SeriesName);
        // ...and the scan carried on to the mapping that does apply.
        Assert.AreEqual("The Stormlight Archive", mapped[1].SeriesName);
        Assert.IsTrue(
            started.Elapsed < TimeSpan.FromSeconds(10),
            $"the mapping scan must be bounded by the match timeout, took {started.Elapsed}");
    }

    /// <summary>
    /// The timeout is applied by construction in SeriesMappingPattern, so it cannot be forgotten
    /// at a call site. Asserted on the compiled Regex itself rather than only through behaviour:
    /// a future call site that builds one with `new Regex(...)` would still pass the test above
    /// on a fast enough machine, but not this one.
    /// </summary>
    [TestMethod]
    public void SeriesMappingPattern_Compile_CarriesTheMatchTimeout()
    {
        var regex = SeriesMappingPattern.Compile("^Stormlight.*");

        Assert.AreEqual(SeriesMappingPattern.MatchTimeout, regex.MatchTimeout);
        Assert.AreNotEqual(System.Text.RegularExpressions.Regex.InfiniteMatchTimeout, regex.MatchTimeout);
    }

    /// <summary>
    /// An ordinary pattern compiles onto the linear-time engine, where backtracking - and so the
    /// timeout - cannot happen at all. This is what keeps the per-match timeout from multiplying
    /// across a mapping set: a pattern here is never part of that cost.
    /// </summary>
    [TestMethod]
    public void SeriesMappingPattern_Compile_UsesTheLinearTimeEngineWhenThePatternAllowsIt()
    {
        foreach (var pattern in new[] { "^Stormlight.*", "^(a+)+$", "Mistborn|Wax and Wayne" })
        {
            var regex = SeriesMappingPattern.Compile(pattern);

            Assert.IsFalse(
                SeriesMappingPattern.CanBacktrack(regex),
                $"'{pattern}' should compile onto the NonBacktracking engine");
        }
    }

    /// <summary>
    /// A pattern using a construct that engine does not implement still compiles - the engine
    /// choice must never change which patterns are valid, only how the pathological ones behave.
    /// These are the ones the timeout remains load-bearing for.
    /// </summary>
    [TestMethod]
    public void SeriesMappingPattern_Compile_FallsBackForConstructsTheLinearEngineLacks()
    {
        // A lookahead and a backreference: both legal regex, neither supported by NonBacktracking.
        foreach (var pattern in new[] { "^(?!Skip)Storm.*", @"^(\w+) \1$" })
        {
            var regex = SeriesMappingPattern.Compile(pattern);

            Assert.IsTrue(
                SeriesMappingPattern.CanBacktrack(regex),
                $"'{pattern}' should fall back to the classic engine");
            Assert.AreEqual(SeriesMappingPattern.MatchTimeout, regex.MatchTimeout);
        }

        Assert.IsTrue(SeriesMappingPattern.Compile("^(?!Skip)Storm.*").IsMatch("Stormlight"));
        Assert.IsFalse(SeriesMappingPattern.Compile("^(?!Skip)Storm.*").IsMatch("SkipStorm"));
    }

    /// <summary>
    /// A pattern that does time out is disabled for the rest of the scope rather than merely
    /// skipped for the one result. The timeout is per match and the scan runs once per scraped
    /// result, so a pattern that stayed enabled would charge its full timeout on every result of
    /// every search in the request. Asserted as a cost ratio rather than an absolute duration:
    /// twenty results must not cost twenty timeouts.
    /// </summary>
    [TestMethod]
    public async Task MapBookSeries_APatternThatTimesOut_IsDisabledForTheRestOfTheScope()
    {
        var evilOwner = new Series { Name = "Evil" };
        _db.Series.Add(evilOwner);
        _db.SaveChanges();
        // A lookahead forces the classic engine, and the nested quantifier then blows up on it -
        // the only shape that can reach the timeout at all now.
        _db.SeriesMappings.Add(new SeriesMapping(default, "^(?!x)(a+)+$", false, evilOwner.Id));
        await _db.SaveChangesAsync();

        var mapper = CreateMapper();
        var pathological = new string('a', 40) + "!";
        var results = Results(Enumerable.Repeat(pathological, 20).ToArray());

        var started = System.Diagnostics.Stopwatch.StartNew();
        var mapped = await mapper.MapBookSeries(results);
        started.Stop();

        Assert.AreEqual(20, mapped.Count);
        // Twenty results, one timeout: well under the twenty the un-disabled pattern would cost.
        Assert.IsTrue(
            started.Elapsed < TimeSpan.FromMilliseconds(SeriesMappingPattern.MatchTimeout.TotalMilliseconds * 10),
            $"a timed-out pattern must not be re-run for every result, took {started.Elapsed}");
    }

    /// <summary>
    /// The mapping set is bounded at the query boundary. It is scanned once per scraped result, so
    /// its size is a multiplier on every metadata search and cannot be left to grow freely. The
    /// cap keeps the lowest ids, so which patterns survive it is stable rather than whatever order
    /// the database happened to return.
    /// </summary>
    [TestMethod]
    public async Task MapBookSeries_MoreMappingsThanTheCap_AppliesTheLowestIdsAndIgnoresTheRest()
    {
        var owner = _db.Series.Single(s => s.Name == "The Stormlight Archive");

        // Fillers that match nothing, then one that would match - deliberately last, so its id is
        // past the cap. Setup already seeded two rows, so this crosses MaxMappings.
        _db.SeriesMappings.AddRange(
            Enumerable.Range(0, BookSeriesMapper.MaxMappings)
                .Select(i => new SeriesMapping(default, $"^never-matches-{i}$", false, owner.Id)));
        await _db.SaveChangesAsync();
        _db.SeriesMappings.Add(new SeriesMapping(default, "^Way of Kings.*", false, owner.Id));
        await _db.SaveChangesAsync();

        var mapper = CreateMapper();

        var mapped = await mapper.MapBookSeries(Results("Way of Kings", "Stormlight Archive"));

        // Past the cap, so it never gets to apply: the value passes through unmapped.
        Assert.AreEqual("Way of Kings", mapped[0].SeriesName);
        // Seeded first (id 1), so it is still within the cap and still applies.
        Assert.AreEqual("The Stormlight Archive", mapped[1].SeriesName);
    }

    /// <summary>
    /// TryCompile reports a syntax error instead of throwing, which is what lets the write
    /// endpoints refuse the pattern with a message rather than accepting a row that is silently
    /// skipped forever after.
    /// </summary>
    [TestMethod]
    public void SeriesMappingPattern_TryCompile_ReportsASyntaxErrorInsteadOfThrowing()
    {
        Assert.IsFalse(SeriesMappingPattern.TryCompile("([unclosed", out var broken, out var error));
        Assert.IsNull(broken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(error));

        Assert.IsTrue(SeriesMappingPattern.TryCompile("^ok$", out var good, out var noError));
        Assert.IsNotNull(good);
        Assert.IsNull(noError);
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
