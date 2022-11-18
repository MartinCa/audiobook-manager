using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

[Table("persons")]
public class Person
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [Column("name")]
    public string Name { get; set; }

    public List<Audiobook> BooksAuthored { get; set; }

    public List<Audiobook> BooksNarrated { get; set; }

    public Person(long id, string name)
    {
        Id = id;
        Name = name;

        BooksAuthored = new List<Audiobook>();
        BooksNarrated = new List<Audiobook>();
    }
}
