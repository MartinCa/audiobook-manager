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
    public async Task<IList<SeriesMapping>> GetSeriesMappings()
    {
        return await _settingsService.GetSeriesMappings();
    }

    [HttpPost("series_mappings")]
    public async Task<SeriesMapping> CreateSeriesMapping([FromBody] SeriesMapping dto)
    {
        return await _settingsService.CreateSeriesMapping(dto);
    }

    [HttpPut("series_mapping/{mappingId}")]
    public async Task<SeriesMapping> UpdateSeriesMappingAsync([FromBody] SeriesMapping dto, long mappingId)
    {
        return await _settingsService.UpdateSeriesMapping(dto);
    }

    [HttpDelete("series_mapping/{mappingId}")]
    public async Task DeleteSeriesMappingAsync(long mappingId)
    {
        await _settingsService.DeleteSeriesMapping(mappingId);
    }
}
