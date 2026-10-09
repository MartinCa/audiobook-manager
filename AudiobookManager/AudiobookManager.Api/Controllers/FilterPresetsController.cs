using AudiobookManager.Api.Dtos;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookManager.Api.Controllers;

/// <summary>
/// Named, saved filter sets for the library lists (books, series, authors). A preset is the
/// list's filters as its endpoint takes them; applying one is the client putting them back into
/// the list's search params. Every list is bounded by <see cref="FilterPresetRules.MaxPresetsPerScope"/>,
/// so the list endpoint returns everything without paging.
/// </summary>
[Route("api/filter-presets")]
[ApiController]
public class FilterPresetsController : ControllerBase
{
    private readonly IFilterPresetService _service;
    private readonly ILogger<FilterPresetsController> _logger;

    public FilterPresetsController(IFilterPresetService service, ILogger<FilterPresetsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>The presets of one list (<c>books</c>, <c>series</c> or <c>authors</c>), by name.</summary>
    [HttpGet]
    public async Task<ActionResult<List<FilterPresetDto>>> GetPresets([FromQuery] string scope)
    {
        try
        {
            var presets = await _service.GetAsync(scope);
            return Ok(presets.Select(ToDto).ToList());
        }
        catch (ArgumentException ex)
        {
            // Only raised with messages written for the caller (an unknown list).
            return this.InvalidRequest(ex.Message);
        }
    }

    [HttpPost]
    public async Task<ActionResult<FilterPresetDto>> CreatePreset([FromBody] CreateFilterPresetRequest request)
    {
        try
        {
            return Ok(ToDto(await _service.CreateAsync(request.Scope, request.Name, request.Filters)));
        }
        catch (ArgumentException ex)
        {
            return this.InvalidRequest(ex.Message);
        }
        catch (FilterPresetNameTakenException ex)
        {
            return this.ConflictingState(ex.Message, "Preset name taken");
        }
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<FilterPresetDto>> UpdatePreset(long id, [FromBody] UpdateFilterPresetRequest request)
    {
        try
        {
            return Ok(ToDto(await _service.UpdateAsync(id, request.Name, request.Filters)));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return this.InvalidRequest(ex.Message);
        }
        catch (FilterPresetNameTakenException ex)
        {
            return this.ConflictingState(ex.Message, "Preset name taken");
        }
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> DeletePreset(long id)
    {
        // Deleting a preset that is already gone is the state the caller asked for.
        await _service.DeleteAsync(id);
        return Ok();
    }

    private static FilterPresetDto ToDto(FilterPresetInfo preset) =>
        new(preset.Id, preset.Scope, preset.Name, preset.Filters.ToDictionary(kv => kv.Key, kv => kv.Value), preset.UpdatedAt);
}
