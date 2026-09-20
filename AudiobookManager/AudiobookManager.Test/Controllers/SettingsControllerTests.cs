using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;
using DomainInitialsSpacing = AudiobookManager.Domain.InitialsSpacing;
using DomainInitialsPunctuation = AudiobookManager.Domain.InitialsPunctuation;

namespace AudiobookManager.Test.Controllers;

[TestClass]
public class SettingsControllerTests
{
    private SettingsController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _controller = new SettingsController(Mock.Of<ISettingsService>(), Mock.Of<IScheduledTaskService>());
    }

    [TestMethod]
    public void GetLanguages_ReturnsExactlyTheSupportedLanguagesInOrder()
    {
        var result = _controller.GetLanguages();

        CollectionAssert.AreEqual(
            new List<string> { "en", "da" },
            result.Languages.Select(l => l.Code).ToList());
        CollectionAssert.AreEqual(
            new List<string> { "English", "Danish" },
            result.Languages.Select(l => l.DisplayName).ToList());
    }

    [TestMethod]
    public void GetLanguages_ReturnsTheDefaultTheClientSeedsANewBookWith()
    {
        var result = _controller.GetLanguages();

        Assert.AreEqual("en", result.DefaultCode);
        Assert.IsTrue(
            result.Languages.Any(l => l.Code == result.DefaultCode),
            "The default has to be one of the offered options, or the select renders it as unrecognized");
    }

    [TestMethod]
    public void GetLanguages_StaysInStepWithTheDomainList()
    {
        // The frontend holds no list of its own and derives everything from this endpoint, so
        // this is the only thing keeping the two in step.
        var result = _controller.GetLanguages();

        CollectionAssert.AreEqual(
            Languages.Supported.Select(l => l.Code).ToList(),
            result.Languages.Select(l => l.Code).ToList());
        Assert.AreEqual(Languages.DefaultCode, result.DefaultCode);
    }

    [TestMethod]
    public void GetLanguages_ServesEveryAliasThatNormalizeAccepts()
    {
        var result = _controller.GetLanguages();

        // The client folds scraped and tagged values against these, so any spelling Normalize
        // accepts but this endpoint withholds is a spelling the two layers disagree on - the
        // endonym "Dansk" is the case that actually bit, being derivable from neither the code
        // nor the English display name.
        foreach (var language in result.Languages)
        {
            CollectionAssert.AreEquivalent(
                Languages.AliasesFor(language.Code),
                language.Aliases);

            foreach (var alias in language.Aliases)
            {
                Assert.AreEqual(
                    language.Code,
                    Languages.Normalize(alias),
                    $"'{alias}' is served as an alias of '{language.Code}' but does not normalize to it");
            }
        }

        CollectionAssert.Contains(
            result.Languages.Single(l => l.Code == "da").Aliases, "dansk");
    }

    [TestMethod]
    public void GetSystemInfo_ReturnsVersionAndDotNetFramework()
    {
        var result = _controller.GetSystemInfo();

        Assert.IsNotNull(result);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Version));
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.DotNetVersion));
    }

    [TestMethod]
    public async Task GetLibrarySettings_ServesTheServiceValueAsString()
    {
        var service = new Mock<ISettingsService>();
        service
            .Setup(s => s.GetLibrarySettings())
            .ReturnsAsync(new Domain.LibrarySettings { InitialsSpacing = DomainInitialsSpacing.Spaced });
        var controller = new SettingsController(service.Object, Mock.Of<IScheduledTaskService>());

        var result = await controller.GetLibrarySettings();

        var ok = Assert.IsInstanceOfType<OkObjectResult>(result.Result);
        var dto = Assert.IsInstanceOfType<LibrarySettingsDto>(ok.Value);
        Assert.AreEqual("Spaced", dto.InitialsSpacing);
    }

    [TestMethod]
    public async Task UpdateLibrarySettings_ParsesTheEnumNameCaseInsensitively()
    {
        var service = new Mock<ISettingsService>();
        service
            .Setup(s => s.GetLibrarySettings())
            .ReturnsAsync(new Domain.LibrarySettings());
        service
            .Setup(s => s.UpdateLibrarySettings(It.IsAny<Domain.LibrarySettings>()))
            .ReturnsAsync((Domain.LibrarySettings s) => s);
        var controller = new SettingsController(service.Object, Mock.Of<IScheduledTaskService>());

        var result = await controller.UpdateLibrarySettings(new UpdateLibrarySettingsDto("spaced", null, 1000, null, null));

        Assert.IsNotNull(result);
        service.Verify(s => s.UpdateLibrarySettings(
            It.Is<Domain.LibrarySettings>(v => v.InitialsSpacing == DomainInitialsSpacing.Spaced)), Times.Once);
    }

    [TestMethod]
    public async Task UpdateLibrarySettings_ParsesThePunctuationEnumNameCaseInsensitively()
    {
        var service = new Mock<ISettingsService>();
        service
            .Setup(s => s.GetLibrarySettings())
            .ReturnsAsync(new Domain.LibrarySettings());
        service
            .Setup(s => s.UpdateLibrarySettings(It.IsAny<Domain.LibrarySettings>()))
            .ReturnsAsync((Domain.LibrarySettings s) => s);
        var controller = new SettingsController(service.Object, Mock.Of<IScheduledTaskService>());

        var result = await controller.UpdateLibrarySettings(
            new UpdateLibrarySettingsDto("spaced", "undotted", 1000, null, null));

        Assert.IsNotNull(result);
        service.Verify(s => s.UpdateLibrarySettings(
            It.Is<Domain.LibrarySettings>(v => v.InitialsPunctuation == DomainInitialsPunctuation.Undotted)),
            Times.Once);
    }

    [TestMethod]
    public async Task UpdateLibrarySettings_OmittedPunctuation_KeepsTheStoredValue()
    {
        var service = new Mock<ISettingsService>();
        service
            .Setup(s => s.GetLibrarySettings())
            .ReturnsAsync(new Domain.LibrarySettings { InitialsPunctuation = DomainInitialsPunctuation.Undotted });
        service
            .Setup(s => s.UpdateLibrarySettings(It.IsAny<Domain.LibrarySettings>()))
            .ReturnsAsync((Domain.LibrarySettings s) => s);
        var controller = new SettingsController(service.Object, Mock.Of<IScheduledTaskService>());

        await controller.UpdateLibrarySettings(new UpdateLibrarySettingsDto("Spaced", null, 1000, null, null));

        service.Verify(s => s.UpdateLibrarySettings(
            It.Is<Domain.LibrarySettings>(v => v.InitialsPunctuation == DomainInitialsPunctuation.Undotted)),
            Times.Once);
    }

    [TestMethod]
    public async Task UpdateLibrarySettings_UnknownPunctuationValue_ReturnsProblemDetailsWithoutCallingService()
    {
        var service = new Mock<ISettingsService>();
        service.Setup(s => s.GetLibrarySettings()).ReturnsAsync(new Domain.LibrarySettings());
        var controller = new SettingsController(service.Object, Mock.Of<IScheduledTaskService>());

        var result = await controller.UpdateLibrarySettings(
            new UpdateLibrarySettingsDto("Spaced", "Periodic", 1000, null, null));

        ProblemAssert.HasDetail(
            result.Result,
            400,
            "'Periodic' is not a known initials punctuation. Use one of: Dotted, Undotted.");
        service.Verify(s => s.UpdateLibrarySettings(It.IsAny<Domain.LibrarySettings>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateLibrarySettings_UnknownValue_ReturnsProblemDetailsWithoutCallingService()
    {
        var service = new Mock<ISettingsService>();
        var controller = new SettingsController(service.Object, Mock.Of<IScheduledTaskService>());

        var result = await controller.UpdateLibrarySettings(new UpdateLibrarySettingsDto("WidelySpaced", null, 1000, null, null));

        ProblemAssert.HasDetail(
            result.Result,
            400,
            "'WidelySpaced' is not a known initials spacing. Use one of: Spaced, Unspaced.");
        service.Verify(s => s.UpdateLibrarySettings(It.IsAny<Domain.LibrarySettings>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateLibrarySettings_MissingValue_ReturnsProblemDetails()
    {
        var service = new Mock<ISettingsService>();
        var controller = new SettingsController(service.Object, Mock.Of<IScheduledTaskService>());

        var result = await controller.UpdateLibrarySettings(new UpdateLibrarySettingsDto(null!, null, 1000, null, null));

        ProblemAssert.HasStatus(result.Result, 400);
        service.Verify(s => s.UpdateLibrarySettings(It.IsAny<Domain.LibrarySettings>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateLibrarySettings_InvalidCron_ReturnsProblemDetailsWithoutCallingService()
    {
        var service = new Mock<ISettingsService>();
        service.Setup(s => s.GetLibrarySettings()).ReturnsAsync(new Domain.LibrarySettings());
        var controller = new SettingsController(service.Object, Mock.Of<IScheduledTaskService>());

        var result = await controller.UpdateLibrarySettings(
            new UpdateLibrarySettingsDto("Spaced", null, 1000, true, "not a cron expression"));

        ProblemAssert.HasStatus(result.Result, 400);
        service.Verify(s => s.UpdateLibrarySettings(It.IsAny<Domain.LibrarySettings>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateLibrarySettings_ValidCron_RoundTripsTheScheduleFields()
    {
        var service = new Mock<ISettingsService>();
        service.Setup(s => s.GetLibrarySettings()).ReturnsAsync(new Domain.LibrarySettings());
        service
            .Setup(s => s.UpdateLibrarySettings(It.IsAny<Domain.LibrarySettings>()))
            .ReturnsAsync((Domain.LibrarySettings s) => s);
        var controller = new SettingsController(service.Object, Mock.Of<IScheduledTaskService>());

        var result = await controller.UpdateLibrarySettings(
            new UpdateLibrarySettingsDto("Spaced", null, 1000, false, "0 4 * * *"));

        var ok = Assert.IsInstanceOfType<OkObjectResult>(result.Result);
        var dto = Assert.IsInstanceOfType<LibrarySettingsDto>(ok.Value);
        Assert.IsFalse(dto.UpcomingReleasesEnabled);
        Assert.AreEqual("0 4 * * *", dto.UpcomingReleasesCronSchedule);
        service.Verify(s => s.UpdateLibrarySettings(
            It.Is<Domain.LibrarySettings>(v =>
                v.UpcomingReleasesEnabled == false && v.UpcomingReleasesCronSchedule == "0 4 * * *")),
            Times.Once);
    }

    [TestMethod]
    public async Task UpdateLibrarySettings_OmittedScheduleFields_KeepTheStoredValues()
    {
        var service = new Mock<ISettingsService>();
        service
            .Setup(s => s.GetLibrarySettings())
            .ReturnsAsync(new Domain.LibrarySettings
            {
                UpcomingReleasesEnabled = false,
                UpcomingReleasesCronSchedule = "0 5 * * *",
            });
        service
            .Setup(s => s.UpdateLibrarySettings(It.IsAny<Domain.LibrarySettings>()))
            .ReturnsAsync((Domain.LibrarySettings s) => s);
        var controller = new SettingsController(service.Object, Mock.Of<IScheduledTaskService>());

        var result = await controller.UpdateLibrarySettings(new UpdateLibrarySettingsDto("Spaced", null, 1000, null, null));

        var ok = Assert.IsInstanceOfType<OkObjectResult>(result.Result);
        var dto = Assert.IsInstanceOfType<LibrarySettingsDto>(ok.Value);
        Assert.IsFalse(dto.UpcomingReleasesEnabled);
        Assert.AreEqual("0 5 * * *", dto.UpcomingReleasesCronSchedule);
    }

    [TestMethod]
    public async Task GetScheduledTasks_ServesTheServiceValueAsDtos()
    {
        var scheduledTaskService = new Mock<IScheduledTaskService>();
        var nextRun = DateTime.UtcNow.AddHours(1);
        scheduledTaskService
            .Setup(s => s.GetScheduledTasksAsync())
            .ReturnsAsync(new List<Domain.ScheduledTask>
            {
                new(ScheduledTaskKeys.UpcomingReleasesRefresh, "Upcoming Releases Refresh", "0 3 * * *", true, null, null, null, nextRun),
            });
        var controller = new SettingsController(Mock.Of<ISettingsService>(), scheduledTaskService.Object);

        var result = await controller.GetScheduledTasks();

        var ok = Assert.IsInstanceOfType<OkObjectResult>(result.Result);
        var dtos = Assert.IsInstanceOfType<List<ScheduledTaskDto>>(ok.Value);
        Assert.AreEqual(1, dtos.Count);
        Assert.AreEqual(ScheduledTaskKeys.UpcomingReleasesRefresh, dtos[0].Key);
        Assert.AreEqual("Upcoming Releases Refresh", dtos[0].Name);
        Assert.AreEqual("0 3 * * *", dtos[0].CronSchedule);
        Assert.IsTrue(dtos[0].Enabled);
        Assert.AreEqual(nextRun, dtos[0].NextRunAt);
    }
}
