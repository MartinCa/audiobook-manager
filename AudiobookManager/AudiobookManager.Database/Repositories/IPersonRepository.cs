using AudiobookManager.Database.Models;

namespace AudiobookManager.Database.Repositories;
public interface IPersonRepository
{
    Task<Person> GetOrCreatePerson(string name);

    /// <summary>
    /// Batch equivalent of <see cref="GetOrCreatePerson"/>: resolves every distinct name in
    /// <paramref name="names"/> in a single query, creates whichever ones don't already exist
    /// in a single insert, and returns one <see cref="Person"/> per input name (duplicates in
    /// the input collapse to the same instance).
    /// </summary>
    Task<Dictionary<string, Person>> GetOrCreatePersons(IEnumerable<string> names);
    /// <summary>Distinct names of authors that have at least one book. Backs the entry-time
    /// autocomplete, which needs nothing but the strings.</summary>
    Task<List<string>> GetAuthorNamesAsync();

    /// <summary>Every author that has at least one book, with its book count projected in SQL.</summary>
    Task<List<AuthorSummaryRow>> GetAllAuthorSummariesAsync();

    /// <summary>Name-matching authors, with the book count projected in SQL.</summary>
    Task<List<AuthorSummaryRow>> SearchAuthorSummariesAsync(string query, int limit);

    /// <summary>A single author's id/name/book-count, or null when the author does not exist.</summary>
    Task<AuthorSummaryRow?> GetAuthorSummaryAsync(long authorId);

    /// <summary>
    /// Author name -> the id/title of each book they authored. Used by the similar-author
    /// grouping, which needs nothing else off the audiobook rows. Authors recorded under
    /// separate Person rows with the same name are merged into one entry.
    /// </summary>
    Task<Dictionary<string, List<AuthorBookRef>>> GetAuthorBookRefsAsync();
}
