using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

/// <summary>
/// A named, saved set of list filters for one of the library lists (books, series, authors). The
/// filters are stored as the JSON object the list's route search params / query string carry
/// (<c>{"sources":["Unsupported"],"queueStates":["NotQueued"]}</c>), already validated and
/// normalized by <c>Services.FilterPresetService</c>: opaque to the database, owned by the list
/// that reads them back. The search text is deliberately not part of a preset.
///
/// <see cref="Name"/> is unique per <see cref="Scope"/> ignoring case.
/// </summary>
[Table("filter_preset")]
public class FilterPreset
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>Which list the preset belongs to; see <c>Services.FilterPresetScopes</c>.</summary>
    [Required]
    [Column("scope")]
    public string Scope { get; set; } = string.Empty;

    [Required]
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The filter values as a JSON object (validated, normalized).</summary>
    [Required]
    [Column("filters_json")]
    public string FiltersJson { get; set; } = string.Empty;

    [Required]
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Required]
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
