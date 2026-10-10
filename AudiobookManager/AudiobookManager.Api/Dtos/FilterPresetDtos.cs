using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace AudiobookManager.Api.Dtos;

/// <summary>
/// A saved filter preset. <paramref name="Filters"/> holds the list's filter values keyed by the
/// query parameter names its endpoint takes (<c>sources</c>, <c>queueStates</c>, ...), exactly the
/// values the list keeps in its route search params.
/// </summary>
public record FilterPresetDto(
    long Id,
    string Scope,
    string Name,
    Dictionary<string, JsonElement> Filters,
    DateTime UpdatedAt);

public class CreateFilterPresetRequest
{
    /// <summary>The list the preset belongs to: <c>books</c>, <c>series</c> or <c>authors</c>.</summary>
    [Required]
    public string Scope { get; set; } = string.Empty;

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Dictionary<string, JsonElement> Filters { get; set; } = new();
}

/// <summary>Replaces name and filters together - renaming and overwriting are the same call.</summary>
public class UpdateFilterPresetRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Dictionary<string, JsonElement> Filters { get; set; } = new();
}
