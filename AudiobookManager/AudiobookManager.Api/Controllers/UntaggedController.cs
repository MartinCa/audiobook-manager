using AudiobookManager.Api.Dtos;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookManager.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UntaggedController : ControllerBase
{
    private readonly IFileService _untaggedService;
    private readonly ILogger _logger;

    public UntaggedController(IFileService untaggedService, ILogger<UntaggedController> logger)
    {
        this._untaggedService = untaggedService;
        this._logger = logger;
    }

    [HttpGet(Name = "GetUntaggedAudiobookFiles")]
    public PaginatedResult<AudiobookFileInfo> Index(int limit = 20, int offset = 0)
    {
        var allItems = _untaggedService.ScanInputDirectoryForAudiobookFiles();
        var items = allItems.Skip(offset).Take(limit);
        return new PaginatedResult<AudiobookFileInfo>(items.Count(), allItems.Count(), items.ToList());
    }

    //[HttpGet("/test")]
    //public void Test()
    //{
    //    var testData = new Audiobook(new List<Person> { new Person("Test Author"), new Person("Test Author 2") }, "Book Name", 2012)
    //    {
    //        Asin = "TEST_ASIN",
    //        Copyright = "TEST_COPYRIGHT",
    //        Description = "TEST_DESCRIPTION",
    //        Genres = new List<string> { "Nonfiction", "Test Genre" },
    //        Narrators = new List<Person> { new Person("Test Narrator"), new Person("Test Narrator 2") },
    //        Publisher = "TEST_PUBLISHER",
    //        Rating = "4.9",
    //        Series = "TEST_SERIES",
    //        SeriesPart = "2-10",
    //        Subtitle = "TEST_SUBTITLE",
    //        Www = "TEST_WWW"
    //    };

    //    _untaggedService.SaveAudiobookTags("C:\\tools\\abtest\\Hercule Poirot 01 - 2020 - The Mysterious Affair at Styles.m4b", testData);
    //}
}
