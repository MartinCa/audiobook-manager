using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

[TestClass]
public class QualifierIndicatorRepositoryTests
{
    private string _dbPath = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"qualifierindicator-{Guid.NewGuid():N}.db");
        using var db = CreateContext();
        db.Database.Migrate();
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

    [TestMethod]
    public async Task Migration_SeedsTheAudibleWordingsSoTheFeatureWorksOutOfTheBox()
    {
        using var db = CreateContext();

        var rules = await new QualifierIndicatorRepository(db).GetAllAsync();

        CollectionAssert.AreEquivalent(
            new[]
            {
                "Audible|Abridged|abridged",
                "Audible|Dramatized|dramatized",
                "Audible|Dramatized Adaptation|dramatized",
                "Audible|Full-Cast Dramatized Adaptation|dramatized",
            },
            rules.Select(r => $"{r.Source}|{r.Indicator}|{r.QualifierKey}").ToArray());
    }

    [TestMethod]
    public async Task ReplaceAll_ReplacesEveryRowIncludingTheSeededOnes()
    {
        using var db = CreateContext();
        var repository = new QualifierIndicatorRepository(db);

        var saved = await repository.ReplaceAllAsync(new[]
        {
            new QualifierIndicator(0, "Goodreads", "Dramatised", "dramatized"),
        });

        Assert.AreEqual(1, saved.Count);
        using var verify = CreateContext();
        var stored = await new QualifierIndicatorRepository(verify).GetAllAsync();
        Assert.AreEqual("Dramatised", stored.Single().Indicator);
    }

    [TestMethod]
    public async Task ReplaceAll_TheSameIndicatorDifferingOnlyByCaseIsRefusedAndTheOldRulesSurvive()
    {
        using var db = CreateContext();
        var repository = new QualifierIndicatorRepository(db);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => repository.ReplaceAllAsync(new[]
        {
            new QualifierIndicator(0, "Audible", "Abridged", "abridged"),
            new QualifierIndicator(0, "audible", "ABRIDGED", "abridged"),
        }));

        using var verify = CreateContext();
        Assert.AreEqual(4, (await new QualifierIndicatorRepository(verify).GetAllAsync()).Count);
    }

    [TestMethod]
    public async Task ReplaceAll_MoreThanTheCapIsRefused()
    {
        using var db = CreateContext();
        var tooMany = Enumerable.Range(0, QualifierIndicatorRepository.MaxRules + 1)
            .Select(i => new QualifierIndicator(0, "Audible", $"Wording {i}", "abridged"))
            .ToList();

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => new QualifierIndicatorRepository(db).ReplaceAllAsync(tooMany));
    }
}
