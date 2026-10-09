using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

/// <summary>Against real SQLite: the unique name index (NOCASE) is what decides a name clash.</summary>
[TestClass]
public class FilterPresetRepositoryTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private FilterPresetRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"filterpreset-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new FilterPresetRepository(_db);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, $"{_dbPath}-wal", $"{_dbPath}-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static FilterPreset Preset(string scope, string name, string filters = """{"sources":["Unsupported"]}""") =>
        new() { Scope = scope, Name = name, FiltersJson = filters };

    [TestMethod]
    public async Task AddAsync_StoresThePresetWithTimestamps()
    {
        var (result, saved) = await _repository.AddAsync(Preset("books", "Backlog"));

        Assert.AreEqual(FilterPresetWriteResult.Saved, result);
        Assert.IsTrue(saved!.Id > 0);
        Assert.AreNotEqual(default, saved.CreatedAt);
        Assert.AreEqual(saved.CreatedAt, saved.UpdatedAt);
    }

    [TestMethod]
    public async Task AddAsync_SameNameIgnoringCaseInTheSameList_IsNameTaken()
    {
        await _repository.AddAsync(Preset("books", "Backlog"));

        var (result, saved) = await _repository.AddAsync(Preset("books", "BACKLOG"));

        Assert.AreEqual(FilterPresetWriteResult.NameTaken, result);
        Assert.IsNull(saved);
        Assert.AreEqual(1, await _repository.CountAsync("books"));
    }

    [TestMethod]
    public async Task AddAsync_TheSameNameInAnotherList_IsAllowed()
    {
        await _repository.AddAsync(Preset("books", "Backlog"));

        var (result, _) = await _repository.AddAsync(Preset("series", "Backlog"));

        Assert.AreEqual(FilterPresetWriteResult.Saved, result);
    }

    [TestMethod]
    public async Task AddAsync_AfterANameClash_TheContextIsStillUsable()
    {
        await _repository.AddAsync(Preset("books", "Backlog"));
        await _repository.AddAsync(Preset("books", "backlog"));

        var (result, _) = await _repository.AddAsync(Preset("books", "Other"));

        Assert.AreEqual(FilterPresetWriteResult.Saved, result);
        Assert.AreEqual(2, await _repository.CountAsync("books"));
    }

    [TestMethod]
    public async Task GetByScopeAsync_ReturnsOnlyThatListsPresets_AndHonoursTheLimit()
    {
        await _repository.AddAsync(Preset("books", "A"));
        await _repository.AddAsync(Preset("books", "B"));
        await _repository.AddAsync(Preset("books", "C"));
        await _repository.AddAsync(Preset("authors", "Other list"));

        var all = await _repository.GetByScopeAsync("books", 10);
        var capped = await _repository.GetByScopeAsync("books", 2);

        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, all.Select(p => p.Name).ToList());
        CollectionAssert.AreEqual(new[] { "A", "B" }, capped.Select(p => p.Name).ToList());
    }

    [TestMethod]
    public async Task UpdateAsync_ReplacesNameAndFilters_AndBumpsUpdatedAt()
    {
        var (_, created) = await _repository.AddAsync(Preset("books", "Old"));
        await Task.Delay(5);

        var (result, updated) = await _repository.UpdateAsync(created!.Id, "New", """{"genres":["Fantasy"]}""");

        Assert.AreEqual(FilterPresetWriteResult.Saved, result);
        Assert.AreEqual("New", updated!.Name);
        var stored = await _repository.GetByIdAsync(created.Id);
        Assert.AreEqual("""{"genres":["Fantasy"]}""", stored!.FiltersJson);
        Assert.IsTrue(stored.UpdatedAt > stored.CreatedAt);
    }

    [TestMethod]
    public async Task UpdateAsync_RenamingOntoAnotherPresetsName_IsNameTaken_AndChangesNothing()
    {
        await _repository.AddAsync(Preset("books", "One"));
        var (_, two) = await _repository.AddAsync(Preset("books", "Two"));

        var (result, _) = await _repository.UpdateAsync(two!.Id, "ONE", """{"genres":["x"]}""");

        Assert.AreEqual(FilterPresetWriteResult.NameTaken, result);
        var stored = await _repository.GetByIdAsync(two.Id);
        Assert.AreEqual("Two", stored!.Name);
        Assert.AreEqual("""{"sources":["Unsupported"]}""", stored.FiltersJson);
    }

    [TestMethod]
    public async Task UpdateAsync_KeepingTheSameName_IsNotAClash()
    {
        var (_, created) = await _repository.AddAsync(Preset("books", "Same"));

        var (result, _) = await _repository.UpdateAsync(created!.Id, "Same", """{"genres":["x"]}""");

        Assert.AreEqual(FilterPresetWriteResult.Saved, result);
    }

    [TestMethod]
    public async Task UpdateAsync_UnknownId_IsNotFound()
    {
        var (result, preset) = await _repository.UpdateAsync(12345, "Name", "{}");

        Assert.AreEqual(FilterPresetWriteResult.NotFound, result);
        Assert.IsNull(preset);
    }

    [TestMethod]
    public async Task DeleteAsync_RemovesThePreset_AndReportsWhetherThereWasOne()
    {
        var (_, created) = await _repository.AddAsync(Preset("books", "Gone"));

        Assert.IsTrue(await _repository.DeleteAsync(created!.Id));
        Assert.IsFalse(await _repository.DeleteAsync(created.Id));
        Assert.AreEqual(0, await _repository.CountAsync("books"));
    }
}
