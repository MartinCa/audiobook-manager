using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;
using DomainInitialsSpacing = AudiobookManager.Domain.InitialsSpacing;

namespace AudiobookManager.Test.Controllers;

[TestClass]
public class SettingsControllerTests
{
    private SettingsController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _controller = new SettingsController(Mock.Of<ISettingsService>());
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
        var controller = new SettingsController(service.Object);

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
            .Setup(s => s.UpdateLibrarySettings(It.IsAny<Domain.LibrarySettings>()))
            .ReturnsAsync((Domain.LibrarySettings s) => s);
        var controller = new SettingsController(service.Object);

        var result = await controller.UpdateLibrarySettings(new UpdateLibrarySettingsDto("spaced"));

        Assert.IsNotNull(result);
        service.Verify(s => s.UpdateLibrarySettings(
            It.Is<Domain.LibrarySettings>(v => v.InitialsSpacing == DomainInitialsSpacing.Spaced)), Times.Once);
    }

    [TestMethod]
    public async Task UpdateLibrarySettings_UnknownValue_ReturnsProblemDetailsWithoutCallingService()
    {
        var service = new Mock<ISettingsService>();
        var controller = new SettingsController(service.Object);

        var result = await controller.UpdateLibrarySettings(new UpdateLibrarySettingsDto("WidelySpaced"));

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
        var controller = new SettingsController(service.Object);

        var result = await controller.UpdateLibrarySettings(new UpdateLibrarySettingsDto(null!));

        ProblemAssert.HasStatus(result.Result, 400);
        service.Verify(s => s.UpdateLibrarySettings(It.IsAny<Domain.LibrarySettings>()), Times.Never);
    }
}
