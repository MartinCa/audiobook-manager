using System.Data.Common;
using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>
/// The series mapping repository's bounded-list behavior: the per-series pattern list is capped
/// at the query boundary (never materialized whole), per the repo's "no unbounded lists over
/// the wire" invariant. The cap is a curator-sized defensive bound - patterns are
/// human-maintained regex rows, far below it in any real series.
/// </summary>
[TestClass]
public class SeriesMappingRepositoryTests
{
    private string _dbPath = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"seriesmappingrepo-{Guid.NewGuid():N}.db");
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private DatabaseContext CreateContext() =>
        new(new DbContextOptions<DatabaseContext>(), Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath }));

    private DatabaseContext CreateContextWithInterceptor(CountingCommandInterceptor interceptor) =>
        new(
            new DbContextOptionsBuilder<DatabaseContext>().AddInterceptors(interceptor).Options,
            Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath }));

    /// <summary>Counts the SELECTs actually issued against the series_mapping table.</summary>
    private class CountingCommandInterceptor : DbCommandInterceptor
    {
        private int _selects;
        private readonly List<string> _texts = new();

        public int MappingSelects => _selects;

        public string Commands => string.Join(" | ", _texts);

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
            // SQLite writes carry a "RETURNING" clause, which makes them reader commands too - only
            // genuine SELECT statements count as reads here.
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("series_mapping", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _selects);
                lock (_texts)
                {
                    _texts.Add(command.CommandText);
                }
            }
        }
    }

    /// <summary>
    /// Regression guard for the review finding: the series-scoped mappings endpoint used to do a
    /// bare <c>ToListAsync()</c>, so a series owning more patterns than any human actually
    /// maintains transferred every row. The query must be capped at the boundary - a series
    /// seeded past the cap still returns only the cap's worth of rows.
    /// </summary>
    [TestMethod]
    public async Task GetBySeriesNameAsync_CapsTheListAtTheQueryBoundary()
    {
        var overCap = SeriesMappingRepository.MaxMappingsPerSeries + 1;

        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();

            var series = new Series { Name = "Mistborn" };
            seed.Series.Add(series);
            await seed.SaveChangesAsync();

            for (var i = 1; i <= overCap; i++)
            {
                seed.SeriesMappings.Add(new SeriesMapping(default, $"^pattern-{i}$", false, series.Id));
            }
            await seed.SaveChangesAsync();

            // Prove the seed itself is over the cap, so the assertion below is meaningful and
            // not vacuous - a series that had fewer rows than the cap would "pass" a broken query.
            Assert.AreEqual(overCap, await seed.SeriesMappings.CountAsync());
        }

        using (var db = CreateContext())
        {
            var repository = new SeriesMappingRepository(db);

            var mappings = await repository.GetBySeriesNameAsync("Mistborn");

            Assert.AreEqual(SeriesMappingRepository.MaxMappingsPerSeries, mappings.Count,
                "the list must never materialize more than the cap, even when the series owns more");
            // Insertion order is preserved within the capped window.
            Assert.AreEqual("^pattern-1$", mappings[0].Regex);
        }
    }

    [TestMethod]
    public async Task GetBySeriesNameAsync_ReturnsTheWholeListWhenTheSeriesIsUnderTheCap()
    {
        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();

            var series = new Series { Name = "Mistborn" };
            seed.Series.Add(series);
            await seed.SaveChangesAsync();

            seed.SeriesMappings.Add(new SeriesMapping(default, "^first.*$", true, series.Id));
            seed.SeriesMappings.Add(new SeriesMapping(default, "^second.*$", false, series.Id));
            await seed.SaveChangesAsync();
        }

        using (var db = CreateContext())
        {
            var repository = new SeriesMappingRepository(db);

            var mappings = await repository.GetBySeriesNameAsync("Mistborn");

            Assert.AreEqual(2, mappings.Count);
            Assert.AreEqual("^first.*$", mappings[0].Regex);
            Assert.AreEqual("^second.*$", mappings[1].Regex);
        }
    }

    // Regression for the review finding: the service's ownership check used to fetch the row with
    // AsNoTracking and then hand the id to the repository's mutation, which re-fetched the SAME
    // row by FindAsync - two SELECTs per edit/delete. The ownership lookup is tracked now, so
    // FindAsync resolves from the identity map and the second SELECT is never issued. The identity
    // map reuse is observable directly: FindAsync must return the very instance the tracked
    // lookup returned.
    [TestMethod]
    public async Task UpdateSeriesMappingAsync_TrackedOwnershipLookupReusesTheSameRow()
    {
        long mappingId;
        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            var series = new Series { Name = "Mistborn" };
            seed.Series.Add(series);
            await seed.SaveChangesAsync();
            var mapping = new SeriesMapping(default, "^old.*$", false, series.Id);
            seed.SeriesMappings.Add(mapping);
            await seed.SaveChangesAsync();
            mappingId = mapping.Id;
        }

        var interceptor = new CountingCommandInterceptor();
        using (var db = CreateContextWithInterceptor(interceptor))
        {
            var repository = new SeriesMappingRepository(db);

            var fetched = await repository.GetSeriesMappingAsync(mappingId);
            Assert.IsNotNull(fetched);

            var updated = await repository.UpdateSeriesMappingAsync(
                new SeriesMapping(mappingId, "^new.*$", true, fetched!.SeriesId));

            Assert.IsNotNull(updated);
            Assert.AreSame(fetched, updated,
                "FindAsync must resolve to the tracked instance, which is exactly what short-circuits the second SELECT");
            Assert.AreEqual("^new.*$", updated!.Regex);
            Assert.AreEqual(1, interceptor.MappingSelects,
                $"the tracked ownership lookup must also satisfy the mutation's FindAsync - no second SELECT. Commands: {interceptor.Commands}");
        }
    }

    [TestMethod]
    public async Task DeleteSeriesMappingAsync_TrackedOwnershipLookupReusesTheSameRow()
    {
        long mappingId;
        using (var seed = CreateContext())
        {
            await seed.Database.MigrateAsync();
            var series = new Series { Name = "Mistborn" };
            seed.Series.Add(series);
            await seed.SaveChangesAsync();
            var mapping = new SeriesMapping(default, "^old.*$", false, series.Id);
            seed.SeriesMappings.Add(mapping);
            await seed.SaveChangesAsync();
            mappingId = mapping.Id;
        }

        var interceptor = new CountingCommandInterceptor();
        using (var db = CreateContextWithInterceptor(interceptor))
        {
            var repository = new SeriesMappingRepository(db);

            var fetched = await repository.GetSeriesMappingAsync(mappingId);
            Assert.IsNotNull(fetched);

            var deleted = await repository.DeleteSeriesMappingAsync(mappingId);

            Assert.IsTrue(deleted);
            Assert.AreEqual(1, interceptor.MappingSelects,
                "the ownership lookup must be the only SELECT - the mutation's FindAsync reuses the identity map");
        }
    }
}
