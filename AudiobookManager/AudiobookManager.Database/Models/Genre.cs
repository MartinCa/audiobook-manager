using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

[Table("genres")]
public class Genre
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [Column("name")]
    public string Name { get; set; }

    public List<Audiobook> Books { get; set; }

    public Genre(long id, string name)
    {
        Id = id;
        Name = name;

        Books = new List<Audiobook>();
    }
}
