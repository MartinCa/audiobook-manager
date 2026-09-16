using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
}