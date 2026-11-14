using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace AudiobookManager.Test.Controllers;

/// <summary>
/// PUT/GET api/settings/library for the per-source search initials handling and the main-author
/// path setting.
/// </summary>
[TestClass]
public class SettingsControllerSourceSearchTests
{
    private static (SettingsController Controller, Mock<ISettingsService> Service) Make(Domain.LibrarySettings? stored = null)
    {
        var service = new Mock<ISettingsService>();
        service.Setup(s => s.GetLibrarySettings()).ReturnsAsync(stored ?? new Domain.LibrarySettings());
        service.Setup(s => s.UpdateLibrarySettings(It.IsAny<Domain.LibrarySettings>()))
            .ReturnsAsync((Domain.LibrarySettings s) => s);
        return (new SettingsController(service.Object, Mock.Of<IScheduledTaskService>(), Mock.Of<IQualifierIndicatorService>()), service);
    }

    private static UpdateLibrarySettingsDto Update(
        bool? mainAuthorOnly = null, Dictionary<string, string>? bySource = null) =>
        new("Spaced", null, 1000, null, null, null, null, null, null, null, mainAuthorOnly, bySource);

    [TestMethod]
    public async Task GetLibrarySettings_ReportsTheMainAuthorSettingAndOverrides()
    {
        var stored = new Domain.LibrarySettings { MainAuthorOnlyInPath = true };
        stored.SearchInitialsHandlingBySource["Hardcover"] = Domain.SearchInitialsHandling.Compact;
        var (controller, _) = Make(stored);

        var ok = Assert.IsInstanceOfType<OkObjectResult>((await controller.GetLibrarySettings()).Result);
        var dto = Assert.IsInstanceOfType<LibrarySettingsDto>(ok.Value);

        Assert.IsTrue(dto.MainAuthorOnlyInPath);
        Assert.AreEqual("Compact", dto.SearchInitialsHandlingBySource["Hardcover"]);
        Assert.AreEqual(1, dto.SearchInitialsHandlingBySource.Count);
    }

    [TestMethod]
    public async Task UpdateLibrarySettings_SavesTheMainAuthorSetting()
    {
        var (controller, service) = Make();

        await controller.UpdateLibrarySettings(Update(mainAuthorOnly: true));

        service.Verify(s => s.UpdateLibrarySettings(It.Is<Domain.LibrarySettings>(v => v.MainAuthorOnlyInPath)), Times.Once);
    }

    [TestMethod]
    public async Task UpdateLibrarySettings_OmittedMainAuthorSetting_KeepsTheStoredOne_SoAnOlderClientCannotMoveTheLibrary()
    {
        var (controller, service) = Make(new Domain.LibrarySettings { MainAuthorOnlyInPath = true });

        await controller.UpdateLibrarySettings(Update());

        service.Verify(s => s.UpdateLibrarySettings(It.Is<Domain.LibrarySettings>(v => v.MainAuthorOnlyInPath)), Times.Once);
    }

    [TestMethod]
    public async Task UpdateLibrarySettings_ExplicitFalse_TurnsTheMainAuthorSettingOff()
    {
        var (controller, service) = Make(new Domain.LibrarySettings { MainAuthorOnlyInPath = true });

        await controller.UpdateLibrarySettings(Update(mainAuthorOnly: false));

        service.Verify(s => s.UpdateLibrarySettings(It.Is<Domain.LibrarySettings>(v => !v.MainAuthorOnlyInPath)), Times.Once);
    }

    [TestMethod]
    public async Task UpdateLibrarySettings_PerSourceOverrides_ReplaceTheStoredSet()
    {
        var stored = new Domain.LibrarySettings();
        stored.SearchInitialsHandlingBySource["Audible"] = Domain.SearchInitialsHandling.Spaced;
        var (controller, service) = Make(stored);

        await controller.UpdateLibrarySettings(Update(bySource: new Dictionary<string, string> { ["Hardcover"] = "compact" }));

        service.Verify(s => s.UpdateLibrarySettings(It.Is<Domain.LibrarySettings>(v =>
            v.SearchInitialsHandlingBySource.Count == 1 &&
            v.SearchInitialsHandlingBySource["Hardcover"] == Domain.SearchInitialsHandling.Compact)), Times.Once);
    }

    [TestMethod]
    public async Task UpdateLibrarySettings_EmptyOverrides_ClearThemAll()
    {
        var stored = new Domain.LibrarySettings();
        stored.SearchInitialsHandlingBySource["Audible"] = Domain.SearchInitialsHandling.Spaced;
        var (controller, service) = Make(stored);

        await controller.UpdateLibrarySettings(Update(bySource: new Dictionary<string, string>()));

        service.Verify(s => s.UpdateLibrarySettings(It.Is<Domain.LibrarySettings>(v =>
            v.SearchInitialsHandlingBySource.Count == 0)), Times.Once);
    }

    [TestMethod]
    public async Task UpdateLibrarySettings_OmittedOverrides_KeepTheStoredSet()
    {
        var stored = new Domain.LibrarySettings();
        stored.SearchInitialsHandlingBySource["Audible"] = Domain.SearchInitialsHandling.Spaced;
        var (controller, service) = Make(stored);

        await controller.UpdateLibrarySettings(Update());

        service.Verify(s => s.UpdateLibrarySettings(It.Is<Domain.LibrarySettings>(v =>
            v.SearchInitialsHandlingBySource.Count == 1 &&
            v.SearchInitialsHandlingBySource["Audible"] == Domain.SearchInitialsHandling.Spaced)), Times.Once);
    }

    [TestMethod]
    public async Task UpdateLibrarySettings_UnknownHandling_IsRejectedWithoutSaving()
    {
        var (controller, service) = Make();

        var result = await controller.UpdateLibrarySettings(
            Update(bySource: new Dictionary<string, string> { ["Hardcover"] = "Sideways" }));

        var problem = Assert.IsInstanceOfType<ObjectResult>(result.Result);
        Assert.AreEqual(400, problem.StatusCode);
        service.Verify(s => s.UpdateLibrarySettings(It.IsAny<Domain.LibrarySettings>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateLibrarySettings_UnknownSourceFromTheService_IsA400ProblemNotA500()
    {
        var (controller, service) = Make();
        service.Setup(s => s.UpdateLibrarySettings(It.IsAny<Domain.LibrarySettings>()))
            .ThrowsAsync(new ArgumentException("'Nope' is not a known metadata source."));

        var result = await controller.UpdateLibrarySettings(
            Update(bySource: new Dictionary<string, string> { ["Nope"] = "Compact" }));

        var problem = Assert.IsInstanceOfType<ObjectResult>(result.Result);
        Assert.AreEqual(400, problem.StatusCode);
    }
}
