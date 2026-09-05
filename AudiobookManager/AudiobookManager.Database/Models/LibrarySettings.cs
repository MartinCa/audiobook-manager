using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace AudiobookManager.Database.Models;

/// <summary>
/// The single row of UI-editable library-wide settings. Deliberately a typed row rather than a
/// generic key/value table: every future setting gets a column with real type safety, and the
/// "exactly one row" shape is what makes a missing row a bootstrap case rather than a lookup miss.
/// The id is a fixed 1 (not autoincrement), so the primary key itself enforces the singleton
/// shape - a racing second bootstrap insert fails with a UNIQUE violation instead of silently
/// creating a second row.
/// </summary>
[Table("library_settings")]
public class LibrarySettings
{
    public const long SingletonId = 1;

    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long Id { get; set; }

    [Column("initials_spacing")]
    public InitialsSpacing InitialsSpacing { get; set; } = InitialsSpacing.Unspaced;

    public LibrarySettings() { }

    public LibrarySettings(long id, InitialsSpacing initialsSpacing)
    {
        Id = id;
        InitialsSpacing = initialsSpacing;
    }
}
