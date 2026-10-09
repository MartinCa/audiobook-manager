using System.Text.Json;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Services;
using Moq;

namespace AudiobookManager.Test.Services;

[TestClass]
public class FilterPresetServiceTests
{
    private Mock<IFilterPresetRepository> _repository = null!;
    private FilterPresetService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _repository = new Mock<IFilterPresetRepository>();
        _service = new FilterPresetService(_repository.Object);
    }

    private static IReadOnlyDictionary<string, JsonElement> Filters(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    private void RepositoryAcceptsAdds()
    {
        _repository
            .Setup(r => r.AddAsync(It.IsAny<FilterPreset>()))
            .ReturnsAsync((FilterPreset p) =>
            {
                p.Id = 7;
                p.UpdatedAt = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
                return (FilterPresetWriteResult.Saved, p);
            });
    }

    [TestMethod]
    public async Task CreateAsync_StoresTheNormalizedNameScopeAndFilters()
    {
        RepositoryAcceptsAdds();
        FilterPreset? added = null;
        _repository.Setup(r => r.AddAsync(It.IsAny<FilterPreset>()))
            .Callback((FilterPreset p) => added = p)
            .ReturnsAsync((FilterPreset p) => (FilterPresetWriteResult.Saved, p));

        var created = await _service.CreateAsync(
            " Books ", "  Unsupported backlog ",
            Filters("""{"sources":["Unsupported"],"queueStates":["NotQueued"]}"""));

        Assert.AreEqual("books", added!.Scope);
        Assert.AreEqual("Unsupported backlog", added.Name);
        Assert.AreEqual("""{"queueStates":["NotQueued"],"sources":["Unsupported"]}""", added.FiltersJson);
        Assert.AreEqual("Unsupported backlog", created.Name);
        CollectionAssert.AreEquivalent(
            new[] { "queueStates", "sources" }, created.Filters.Keys.ToList());
    }

    [TestMethod]
    public async Task CreateAsync_ATakenName_ThrowsNameTaken()
    {
        _repository.Setup(r => r.AddAsync(It.IsAny<FilterPreset>()))
            .ReturnsAsync((FilterPresetWriteResult.NameTaken, (FilterPreset?)null));

        var ex = await Assert.ThrowsExactlyAsync<FilterPresetNameTakenException>(() =>
            _service.CreateAsync("books", "Backlog", Filters("""{"sources":["Unsupported"]}""")));

        StringAssert.Contains(ex.Message, "Backlog");
    }

    [TestMethod]
    public async Task CreateAsync_AtTheMaximum_IsRefusedWithoutWriting()
    {
        _repository.Setup(r => r.CountAsync("books")).ReturnsAsync(FilterPresetRules.MaxPresetsPerScope);

        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            _service.CreateAsync("books", "One more", Filters("""{"sources":["Unsupported"]}""")));

        StringAssert.Contains(ex.Message, FilterPresetRules.MaxPresetsPerScope.ToString());
        _repository.Verify(r => r.AddAsync(It.IsAny<FilterPreset>()), Times.Never);
    }

    [TestMethod]
    public async Task CreateAsync_InvalidInput_NeverReachesTheRepository()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            _service.CreateAsync("books", "", Filters("""{"sources":["Unsupported"]}""")));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            _service.CreateAsync("books", "Name", Filters("{}")));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            _service.CreateAsync("books", "Name", Filters("""{"followed":true}""")));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            _service.CreateAsync("narrators", "Name", Filters("""{"sources":["x"]}""")));

        _repository.Verify(r => r.AddAsync(It.IsAny<FilterPreset>()), Times.Never);
    }

    [TestMethod]
    public async Task GetAsync_SortsByNameIgnoringCase_NotByCodePoint()
    {
        _repository.Setup(r => r.GetByScopeAsync("books", FilterPresetRules.MaxPresetsPerScope))
            .ReturnsAsync(new List<FilterPreset>
            {
                new() { Id = 1, Scope = "books", Name = "zebra", FiltersJson = """{"sources":["a"]}""" },
                new() { Id = 2, Scope = "books", Name = "Apple", FiltersJson = """{"sources":["a"]}""" },
                new() { Id = 3, Scope = "books", Name = "banana", FiltersJson = """{"sources":["a"]}""" },
            });

        var presets = await _service.GetAsync("books");

        CollectionAssert.AreEqual(new[] { "Apple", "banana", "zebra" }, presets.Select(p => p.Name).ToList());
    }

    [TestMethod]
    public async Task GetAsync_UnknownScope_Throws()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => _service.GetAsync("narrators"));
    }

    [TestMethod]
    public async Task UpdateAsync_ValidatesFiltersAgainstTheStoredPresetsScope()
    {
        _repository.Setup(r => r.GetByIdAsync(3))
            .ReturnsAsync(new FilterPreset { Id = 3, Scope = "books", Name = "Backlog", FiltersJson = """{"sources":["Unsupported"]}""" });

        // 'followed' is a series/author filter; the preset is a book preset.
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            _service.UpdateAsync(3, "Backlog", Filters("""{"followed":true}""")));
        _repository.Verify(r => r.UpdateAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateAsync_ReplacesNameAndFilters()
    {
        _repository.Setup(r => r.GetByIdAsync(3))
            .ReturnsAsync(new FilterPreset { Id = 3, Scope = "series", Name = "Old", FiltersJson = """{"followed":true}""" });
        _repository.Setup(r => r.UpdateAsync(3, "New name", """{"followed":false}"""))
            .ReturnsAsync((FilterPresetWriteResult.Saved, new FilterPreset
            {
                Id = 3, Scope = "series", Name = "New name", FiltersJson = """{"followed":false}""",
            }));

        var updated = await _service.UpdateAsync(3, " New name ", Filters("""{"followed":false}"""));

        Assert.AreEqual("New name", updated.Name);
        Assert.AreEqual(false, updated.Filters["followed"].GetBoolean());
    }

    [TestMethod]
    public async Task UpdateAsync_UnknownId_ThrowsKeyNotFound()
    {
        _repository.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((FilterPreset?)null);

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() =>
            _service.UpdateAsync(99, "Name", Filters("""{"followed":true}""")));
    }

    [TestMethod]
    public async Task UpdateAsync_RenameOntoAnotherPresetsName_ThrowsNameTaken()
    {
        _repository.Setup(r => r.GetByIdAsync(3))
            .ReturnsAsync(new FilterPreset { Id = 3, Scope = "series", Name = "Old", FiltersJson = """{"followed":true}""" });
        _repository.Setup(r => r.UpdateAsync(3, "Taken", It.IsAny<string>()))
            .ReturnsAsync((FilterPresetWriteResult.NameTaken, (FilterPreset?)null));

        await Assert.ThrowsExactlyAsync<FilterPresetNameTakenException>(() =>
            _service.UpdateAsync(3, "Taken", Filters("""{"followed":true}""")));
    }

    [TestMethod]
    public async Task UpdateAsync_PresetDeletedUnderneath_ThrowsKeyNotFound()
    {
        _repository.Setup(r => r.GetByIdAsync(3))
            .ReturnsAsync(new FilterPreset { Id = 3, Scope = "series", Name = "Old", FiltersJson = """{"followed":true}""" });
        _repository.Setup(r => r.UpdateAsync(3, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((FilterPresetWriteResult.NotFound, (FilterPreset?)null));

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() =>
            _service.UpdateAsync(3, "Name", Filters("""{"followed":true}""")));
    }
}
