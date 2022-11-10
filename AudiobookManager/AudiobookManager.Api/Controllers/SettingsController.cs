using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookManager.Api.Controllers;
[Route("api/[controller]")]
[ApiController]
public class SettingsController : ControllerBase
{
    private readonly ISettingsService _settingsService;

    public SettingsController(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    [HttpGet("series_mappings")]
    public IList<SeriesMapping> GetSeriesMappings()
    {
        return _settingsService.GetSeriesMappings();
    }

    [HttpPost("series_mappings")]
    public SeriesMapping CreateSeriesMapping([FromBody] SeriesMapping dto)
    {
        return _settingsService.CreateSeriesMapping(dto);
    }

    [HttpPut("series_mapping/{mappingId}")]
    public SeriesMapping UpdateSeriesMapping([FromBody] SeriesMapping dto, long mappingId)
    {
        return _settingsService.UpdateSeriesMapping(dto);
    }

    [HttpDelete("series_mapping/{mappingId}")]
    public void DeleteSeriesMapping(long mappingId)
    {
        _settingsService.DeleteSeriesMapping(mappingId);
    }
}
