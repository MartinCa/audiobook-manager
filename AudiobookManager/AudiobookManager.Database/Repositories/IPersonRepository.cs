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
    Task<List<Person>> GetAllAuthorsAsync();
    Task<List<Person>> SearchAuthorsAsync(string query, int limit);
    Task<Person?> GetAuthorWithBooksAsync(long authorId);
}
