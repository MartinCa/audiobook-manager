using System.Text.Json;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.Controllers;

[TestClass]
public class FilterPresetsControllerTests
{
    private Mock<IFilterPresetService> _service = null!;
    private FilterPresetsController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _service = new Mock<IFilterPresetService>();
        _controller = new FilterPresetsController(_service.Object, Mock.Of<ILogger<FilterPresetsController>>());
    }

    private static Dictionary<string, JsonElement> Filters(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    private static FilterPresetInfo Info(long id = 1, string name = "Backlog") =>
        new(id, "books", name, Filters("""{"sources":["Unsupported"]}"""), new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc));

    [TestMethod]
    public async Task GetPresets_ReturnsTheListsPresetsAsDtos()
    {
        _service.Setup(s => s.GetAsync("books")).ReturnsAsync(new List<FilterPresetInfo> { Info(1, "A"), Info(2, "B") });

        var result = await _controller.GetPresets("books");

        var dtos = Assert.IsInstanceOfType<List<FilterPresetDto>>(Assert.IsInstanceOfType<OkObjectResult>(result.Result).Value);
        CollectionAssert.AreEqual(new[] { "A", "B" }, dtos.Select(d => d.Name).ToList());
        Assert.AreEqual("Unsupported", dtos[0].Filters["sources"][0].GetString());
    }

    [TestMethod]
    public async Task GetPresets_UnknownScope_Is400WithTheMessage()
    {
        _service.Setup(s => s.GetAsync("narrators")).ThrowsAsync(new ArgumentException("'narrators' is not a list that supports presets."));

        var result = await _controller.GetPresets("narrators");

        ProblemAssert.HasDetail(result.Result, 400, "'narrators' is not a list that supports presets.");
    }

    [TestMethod]
    public async Task CreatePreset_PassesTheRequestToTheService_AndReturnsTheSavedPreset()
    {
        var filters = Filters("""{"sources":["Unsupported"]}""");
        _service.Setup(s => s.CreateAsync("books", "Backlog", filters)).ReturnsAsync(Info(5, "Backlog"));

        var result = await _controller.CreatePreset(new CreateFilterPresetRequest { Scope = "books", Name = "Backlog", Filters = filters });

        var dto = Assert.IsInstanceOfType<FilterPresetDto>(Assert.IsInstanceOfType<OkObjectResult>(result.Result).Value);
        Assert.AreEqual(5, dto.Id);
        Assert.AreEqual("Backlog", dto.Name);
    }

    [TestMethod]
    public async Task CreatePreset_InvalidInput_Is400WithTheMessageTheUserCanActOn()
    {
        _service.Setup(s => s.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, JsonElement>>()))
            .ThrowsAsync(new ArgumentException("There are no active filters to save. Set at least one filter first."));

        var result = await _controller.CreatePreset(new CreateFilterPresetRequest { Scope = "books", Name = "x" });

        ProblemAssert.HasDetail(result.Result, 400, "There are no active filters to save. Set at least one filter first.");
    }

    [TestMethod]
    public async Task CreatePreset_ATakenName_Is409()
    {
        _service.Setup(s => s.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, JsonElement>>()))
            .ThrowsAsync(new FilterPresetNameTakenException("Backlog"));

        var result = await _controller.CreatePreset(new CreateFilterPresetRequest { Scope = "books", Name = "Backlog" });

        ProblemAssert.HasDetail(result.Result, 409, "A preset named 'Backlog' already exists for this list.");
    }

    [TestMethod]
    public async Task UpdatePreset_ReturnsTheUpdatedPreset()
    {
        var filters = Filters("""{"sources":["Unsupported"]}""");
        _service.Setup(s => s.UpdateAsync(5, "Renamed", filters)).ReturnsAsync(Info(5, "Renamed"));

        var result = await _controller.UpdatePreset(5, new UpdateFilterPresetRequest { Name = "Renamed", Filters = filters });

        Assert.AreEqual("Renamed", Assert.IsInstanceOfType<FilterPresetDto>(Assert.IsInstanceOfType<OkObjectResult>(result.Result).Value).Name);
    }

    [TestMethod]
    public async Task UpdatePreset_UnknownId_Is404()
    {
        _service.Setup(s => s.UpdateAsync(9, It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, JsonElement>>()))
            .ThrowsAsync(new KeyNotFoundException());

        var result = await _controller.UpdatePreset(9, new UpdateFilterPresetRequest { Name = "x" });

        Assert.IsInstanceOfType<NotFoundResult>(result.Result);
    }

    [TestMethod]
    public async Task UpdatePreset_ATakenName_Is409_AndInvalidFiltersAre400()
    {
        _service.Setup(s => s.UpdateAsync(1, "Taken", It.IsAny<IReadOnlyDictionary<string, JsonElement>>()))
            .ThrowsAsync(new FilterPresetNameTakenException("Taken"));
        _service.Setup(s => s.UpdateAsync(1, "Bad", It.IsAny<IReadOnlyDictionary<string, JsonElement>>()))
            .ThrowsAsync(new ArgumentException("'followed' is not a filter of the books list."));

        ProblemAssert.HasStatus((await _controller.UpdatePreset(1, new UpdateFilterPresetRequest { Name = "Taken" })).Result, 409);
        ProblemAssert.HasDetail(
            (await _controller.UpdatePreset(1, new UpdateFilterPresetRequest { Name = "Bad" })).Result,
            400, "'followed' is not a filter of the books list.");
    }

    [TestMethod]
    public async Task DeletePreset_IsOkWhetherOrNotThePresetExisted()
    {
        _service.Setup(s => s.DeleteAsync(1)).ReturnsAsync(true);
        _service.Setup(s => s.DeleteAsync(2)).ReturnsAsync(false);

        Assert.IsInstanceOfType<OkResult>(await _controller.DeletePreset(1));
        Assert.IsInstanceOfType<OkResult>(await _controller.DeletePreset(2));
    }
}
