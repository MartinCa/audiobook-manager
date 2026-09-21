using System.Linq.Expressions;
using System.Reflection;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Search;
using Microsoft.EntityFrameworkCore;

namespace AudiobookManager.Database.Repositories;
public class PersonRepository : IPersonRepository
{
    private readonly DatabaseContext _db;

    private static readonly ConstructorInfo AuthorSummaryRowConstructor =
        typeof(AuthorSummaryRow).GetConstructor(new[] { typeof(long), typeof(string), typeof(int) })
        ?? throw new InvalidOperationException("AuthorSummaryRow positional constructor not found");

    public PersonRepository(DatabaseContext db)
    {
        _db = db;
    }

    public async Task<Dictionary<string, Person>> GetByNamesAsync(IReadOnlyCollection<string> names)
    {
        var result = new Dictionary<string, Person>(StringComparer.Ordinal);
        if (names.Count == 0)
        {
            return result;
        }

        // One IN clause per chunk, at the codebase's shared in-clause size (see
        // ExpectedBookRepository.MaxInClauseIdsPerQuery): a roster's distinct author names are a
        // handful, but the method must not let a pathological set build a single over-limit query.
        // Accent folding is deliberately not applied - the caller passes the source's exact
        // spelling and persons.name is unique, so an exact-match IN is the whole lookup.
        foreach (var chunk in names.ToList().Chunk(ExpectedBookRepository.MaxInClauseIdsPerQuery))
        {
            var rows = await _db.Persons.Where(p => chunk.Contains(p.Name)).ToListAsync();
            foreach (var person in rows)
            {
                result[person.Name] = person;
            }
        }

        return result;
    }

    public async Task<Person> GetOrCreatePerson(string name)
    {
        var dbPerson = await _db.Persons.SingleOrDefaultAsync(p => p.Name == name)
            ?? new Person(default, name);

        if (dbPerson.Id == default)
        {
            _db.Persons.Add(dbPerson);
            await _db.SaveChangesAsync();
        }

        return dbPerson;
    }

    public async Task<Dictionary<string, Person>> GetOrCreatePersons(IEnumerable<string> names)
    {
        var distinctNames = names.Distinct().ToList();
        var result = new Dictionary<string, Person>();
        if (distinctNames.Count == 0)
        {
            return result;
        }

        var existing = await _db.Persons.Where(p => distinctNames.Contains(p.Name)).ToListAsync();
        foreach (var person in existing)
        {
            result[person.Name] = person;
        }

        var missingNames = distinctNames.Where(n => !result.ContainsKey(n)).ToList();
        if (missingNames.Count > 0)
        {
            var newPersons = missingNames.Select(n => new Person(default, n)).ToList();
            _db.Persons.AddRange(newPersons);

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (SqliteErrors.IsUniqueViolation(ex))
            {
                // persons.name is unique, and this method reads then inserts across an await
                // from a request-scoped context. Organizes genuinely run concurrently (the bulk
                // import fans out, OrganizeWorker runs alongside an interactive save), so two
                // of them can both see a new author as missing and both insert it. The loser of
                // that race must adopt the winner's row rather than failing the whole organize
                // and leaving the file half-processed.
                foreach (var entry in _db.ChangeTracker.Entries<Person>()
                             .Where(e => e.State == EntityState.Added)
                             .ToList())
                {
                    entry.State = EntityState.Detached;
                }

                var raced = await _db.Persons
                    .Where(p => missingNames.Contains(p.Name))
                    .ToListAsync();

                foreach (var person in raced)
                {
                    result[person.Name] = person;
                }

                if (missingNames.Any(n => !result.ContainsKey(n)))
                {
                    // Some other constraint failed - not the race this handler is for.
                    throw;
                }

                return result;
            }

            foreach (var person in newPersons)
            {
                result[person.Name] = person;
            }
        }

        return result;
    }

    public async Task<List<string>> GetAuthorNamesAsync()
    {
        // Project in SQL, but sort in memory. SQLite's default BINARY collation orders by code
        // point, so "Zadie" sorts before "alice" and every accented name lands after "Z" - which
        // is user-visible nonsense in the pick-from-a-list autocomplete this feeds. The row set
        // is a flat list of distinct names, so sorting it here costs nothing.
        var names = await _db.Persons
            .AsNoTracking()
            .Where(p => p.BooksAuthored.Any())
            .Select(p => p.Name)
            .Distinct()
            .ToListAsync();

        names.Sort(StringComparer.InvariantCulture);
        return names;
    }

    /// <summary>
    /// The author whose folded name equals the input's folded name, or null. Folded-column
    /// equality via SQLite LIKE semantics (no wildcards, ESCAPE applied): case-insensitive for
    /// ASCII and accent-insensitive for everything, which is exactly what the accent-insensitive
    /// search invariant asks of a "does this value already exist" answer. Only persons that
    /// actually author books count - a narrator-only or orphan (bookless) person is not an
    /// author entry.
    /// </summary>
    public Task<AuthorSummaryRow?> FindAuthorByFoldedNameAsync(string value) =>
        FindPersonByFoldedNameAsync(value, p => p.BooksAuthored.Any(), p => p.BooksAuthored.Count);

    /// <summary>
    /// The narrator whose folded name equals the input's folded name, or null - the narrator
    /// counterpart of <see cref="FindAuthorByFoldedNameAsync"/>, scoped to persons that actually
    /// narrate books. Backs the narrator entry-status classification in the edit form.
    /// </summary>
    public Task<AuthorSummaryRow?> FindNarratorByFoldedNameAsync(string value) =>
        FindPersonByFoldedNameAsync(value, p => p.BooksNarrated.Any(), p => p.BooksNarrated.Count);

    private async Task<AuthorSummaryRow?> FindPersonByFoldedNameAsync(
        string value,
        Expression<Func<Person, bool>> hasLinkedBooks,
        Expression<Func<Person, int>> countSelector)
    {
        var folded = AccentFolding.FoldPlain(value?.Trim());
        if (string.IsNullOrEmpty(folded))
        {
            return null;
        }

        var pattern = LikePatterns.EscapeLikePattern(folded);
        return await _db.Persons
            .AsNoTracking()
            .Where(hasLinkedBooks)
            .Where(p => p.NameFolded != null
                && EF.Functions.Like(p.NameFolded, pattern, LikePatterns.EscapeCharacter))
            .Select(AuthorSummaryProjection(countSelector))
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// The distinct author names the entry-status classification scores as "similar" candidates,
    /// capped at <paramref name="limit"/> rows. The prefilter is bounded by construction - it
    /// never materializes the library's whole name list - and deliberately permissive: rows whose
    /// folded name contains the folded query OR the query's first token (the common typo shape is
    /// one token right, one misspelled), with full-containment rows ordered ahead of token-only
    /// ones so a common token cannot crowd out the genuine match. The fuzzy scoring that decides
    /// what counts as "similar" then runs over this capped set.
    /// </summary>
    public Task<List<AuthorSummaryRow>> SearchAuthorNamesAsync(string query, int limit) =>
        SearchPersonNamesAsync(query, limit, p => p.BooksAuthored.Any(), p => p.BooksAuthored.Count);

    /// <summary>
    /// The narrator counterpart of <see cref="SearchAuthorNamesAsync"/>: bounded, permissive
    /// narrator-name candidates for the narrator entry-status classification. Every person row
    /// is a shared table entry, so the "who is a narrator" filter is the one thing that differs
    /// from the author prefilter.
    /// </summary>
    public Task<List<AuthorSummaryRow>> SearchNarratorNamesAsync(string query, int limit) =>
        SearchPersonNamesAsync(query, limit, p => p.BooksNarrated.Any(), p => p.BooksNarrated.Count);

    private async Task<List<AuthorSummaryRow>> SearchPersonNamesAsync(
        string query,
        int limit,
        Expression<Func<Person, bool>> hasLinkedBooks,
        Expression<Func<Person, int>> countSelector)
    {
        var folded = AccentFolding.FoldPlain(query?.Trim());
        if (string.IsNullOrEmpty(folded))
        {
            return new List<AuthorSummaryRow>();
        }

        var firstToken = FoldFirstToken(folded);

        // Both patterns are ESCAPEd so a literal '%' or '_' the user typed (e.g. "100%", "my_author")
        // matches rows containing exactly that character instead of acting as a LIKE wildcard.
        var fullPattern = $"%{LikePatterns.EscapeLikePattern(folded)}%";
        var tokenPattern = $"%{LikePatterns.EscapeLikePattern(firstToken)}%";
        var hasFirstToken = firstToken.Length > 0;

        var rows = await _db.Persons
            .AsNoTracking()
            .Where(hasLinkedBooks)
            .Where(p => p.NameFolded != null && (
                EF.Functions.Like(p.NameFolded, fullPattern, LikePatterns.EscapeCharacter)
                || (hasFirstToken && EF.Functions.Like(p.NameFolded, tokenPattern, LikePatterns.EscapeCharacter))))
            .OrderByDescending(p => EF.Functions.Like(p.NameFolded, fullPattern, LikePatterns.EscapeCharacter))
            .ThenBy(p => p.Name)
            .Take(limit)
            .Select(AuthorSummaryProjection(countSelector))
            .ToListAsync();

        // Same culture-aware ordering as GetAuthorNamesAsync - a candidate list a human reads
        // must not come back in SQLite's code-point BINARY order.
        return rows.OrderBy(r => r.Name, StringComparer.InvariantCulture).ToList();
    }

    private static string FoldFirstToken(string folded) =>
        folded.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

    /// <summary>
    /// The <see cref="AuthorSummaryRow"/> projection for a person query, with the count
    /// expression taken from the caller (BooksAuthored for author lookups, BooksNarrated for
    /// narrator lookups) - a narrator's row must report narrated books, not authored ones. The
    /// count selector's own parameter is reused as the projection's parameter, so the body needs
    /// no rebinding and EF translates it as ordinary inline navigation-count SQL.
    /// </summary>
    private static Expression<Func<Person, AuthorSummaryRow>> AuthorSummaryProjection(
        Expression<Func<Person, int>> countSelector)
    {
        var p = countSelector.Parameters[0];
        return Expression.Lambda<Func<Person, AuthorSummaryRow>>(
            Expression.New(
                AuthorSummaryRowConstructor,
                Expression.Property(p, nameof(Person.Id)),
                Expression.Property(p, nameof(Person.Name)),
                countSelector.Body),
            p);
    }

    public async Task<List<AuthorSummaryRow>> GetAllAuthorSummariesAsync()
    {
        var rows = await _db.Persons
            .AsNoTracking()
            .Where(p => p.BooksAuthored.Any())
            .Select(p => new AuthorSummaryRow(
                p.Id, p.Name, p.BooksAuthored.Count,
                p.MatchedSourceId != null && p.MatchedSourceId != "", p.MatchedSourceName))
            .ToListAsync();

        // Project in SQL, order in memory. This list is unpaged, so nothing forces the sort
        // into SQL - and SQLite's BINARY collation would order it by code point, putting
        // "Zadie" before "alice" and every accented surname after "Z". Same reasoning (and the
        // same comparer) as GetAuthorNamesAsync.
        return rows.OrderBy(r => r.Name, StringComparer.InvariantCulture).ToList();
    }

    public async Task<(List<AuthorSummaryRow> Items, int Total)> GetAuthorSummariesPagedAsync(
        string? search, int limit, int offset,
        AuthorSummaryFilter? filter = null, IReadOnlyCollection<long>? restrictToIds = null,
        IReadOnlyCollection<long>? excludeIds = null)
    {
        var dbQuery = _db.Persons
            .AsNoTracking()
            .Where(p => p.BooksAuthored.Any());

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Folded on the precomputed NameFolded column like every other search in this
            // repository; the page is ordered in SQL, so it gets BINARY collation (the
            // documented tradeoff for a paged query - see the ordering rule in AGENTS.md).
            // ESCAPEd like every other raw user-pattern LIKE: a literal '%' or '_' the user
            // typed must match the literal character, not act as a wildcard.
            var pattern = $"%{LikePatterns.EscapeLikePattern(AccentFolding.FoldPlain(search!.Trim()))}%";
            dbQuery = dbQuery.Where(p => EF.Functions.Like(p.NameFolded, pattern, LikePatterns.EscapeCharacter));
        }

        if (filter?.Followed is not null)
        {
            var followedIds = _db.AuthorFollows.AsNoTracking().Select(f => f.PersonId);
            dbQuery = filter.Followed == true
                ? dbQuery.Where(p => followedIds.Contains(p.Id))
                : dbQuery.Where(p => !followedIds.Contains(p.Id));
        }

        if (filter?.Matched is not null)
        {
            dbQuery = filter.Matched == true
                ? dbQuery.Where(p => p.MatchedSourceId != null && p.MatchedSourceId != "")
                : dbQuery.Where(p => p.MatchedSourceId == null || p.MatchedSourceId == "");
        }

        if (filter?.Sources is { Count: > 0 } sources)
        {
            // "Unsupported" (AuthorSummaryFilter.UnsupportedSource) is the synthetic bucket for
            // an unmatched author - there is no real MatchedSourceName to compare against, so it
            // is handled separately from the real source names in the set.
            var wantsUnsupported = sources.Contains(AuthorSummaryFilter.UnsupportedSource);
            var realSources = sources.Where(s => s != AuthorSummaryFilter.UnsupportedSource).ToList();

            dbQuery = dbQuery.Where(p =>
                (realSources.Count > 0 && p.MatchedSourceName != null && realSources.Contains(p.MatchedSourceName))
                || (wantsUnsupported && (p.MatchedSourceName == null || p.MatchedSourceName == "")));
        }

        if (filter?.NeverRefreshed == true)
        {
            dbQuery = dbQuery.Where(p => p.LastRefreshedAt == null);
        }
        else if (filter?.NeverRefreshed == false)
        {
            dbQuery = dbQuery.Where(p => p.LastRefreshedAt != null);
        }

        if (filter?.RefreshedAfter is not null)
        {
            dbQuery = dbQuery.Where(p => p.LastRefreshedAt != null && p.LastRefreshedAt >= filter.RefreshedAfter);
        }

        if (filter?.RefreshedBefore is not null)
        {
            // The UI sends a calendar date (day granularity), which model-binds to that day's
            // midnight - a plain "<=" would exclude every refresh later that same day. Treat the
            // bound as "before the day after", so the whole chosen day is included, symmetric
            // with RefreshedAfter's inclusive ">=" against that day's midnight.
            var exclusiveUpperBound = filter.RefreshedBefore.Value.Date.AddDays(1);
            dbQuery = dbQuery.Where(p => p.LastRefreshedAt != null && p.LastRefreshedAt < exclusiveUpperBound);
        }

        if (filter?.MinBookCount is not null)
        {
            dbQuery = dbQuery.Where(p => p.BooksAuthored.Count >= filter.MinBookCount);
        }

        if (filter?.MaxBookCount is not null)
        {
            dbQuery = dbQuery.Where(p => p.BooksAuthored.Count <= filter.MaxBookCount);
        }

        if (restrictToIds is not null)
        {
            dbQuery = dbQuery.Where(p => restrictToIds.Contains(p.Id));
        }

        if (excludeIds is not null)
        {
            dbQuery = dbQuery.Where(p => !excludeIds.Contains(p.Id));
        }

        var total = await dbQuery.CountAsync();

        // BookCount (BooksAuthored.Count) is part of the total order too, defensively: persons
        // names are unique, but the issue asked for it and a future without the unique index
        // must not silently get a non-total order. Id is the final tiebreaker that makes the
        // order total.
        var rows = await dbQuery
            .OrderBy(p => p.Name)
            .ThenByDescending(p => p.BooksAuthored.Count)
            .ThenBy(p => p.Id)
            .Skip(offset)
            .Take(limit)
            .Select(p => new AuthorSummaryRow(
                p.Id, p.Name, p.BooksAuthored.Count,
                p.MatchedSourceId != null && p.MatchedSourceId != "", p.MatchedSourceName))
            .ToListAsync();

        return (rows, total);
    }

    public async Task<(List<AuthorSummaryRow> Items, int Total)> SearchAuthorSummariesAsync(string query, int limit, int offset)
    {
        var folded = AccentFolding.FoldPlain(query);
        // ESCAPEd like every other raw user-pattern LIKE in this repository; the trailing '%' of
        // prefixPattern is the only intentional wildcard.
        var pattern = $"%{LikePatterns.EscapeLikePattern(folded)}%";
        var prefixPattern = $"{LikePatterns.EscapeLikePattern(folded)}%";

        var dbQuery = _db.Persons
            .AsNoTracking()
            .Where(p => p.BooksAuthored.Any() && EF.Functions.Like(p.NameFolded, pattern, LikePatterns.EscapeCharacter));

        var total = await dbQuery.CountAsync();

        var rows = await dbQuery
            // Rank before the limit. This query is capped at `limit` rows, so ordering
            // alphabetically and re-ranking the survivors in the controller discarded the
            // prefix matches the user was most likely reaching for - see SearchAsync.
            .OrderByDescending(p => EF.Functions.Like(p.NameFolded, prefixPattern, LikePatterns.EscapeCharacter))
            .ThenBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Skip(offset)
            .Take(limit)
            .Select(p => new AuthorSummaryRow(
                p.Id, p.Name, p.BooksAuthored.Count,
                p.MatchedSourceId != null && p.MatchedSourceId != "", p.MatchedSourceName))
            .ToListAsync();

        return (rows, total);
    }

    public async Task<AuthorSummaryRow?> GetAuthorSummaryAsync(long authorId)
    {
        return await _db.Persons
            .AsNoTracking()
            .Where(p => p.Id == authorId)
            .Select(p => new AuthorSummaryRow(
                p.Id, p.Name, p.BooksAuthored.Count,
                p.MatchedSourceId != null && p.MatchedSourceId != "", p.MatchedSourceName))
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Author name -> how many books carry that author name, for only the names in
    /// <paramref name="authorNames"/>. The similar-author detection pages its groups, so only
    /// the current page's candidate names need counts - the old implementation loaded every
    /// author's full book list to derive the same numbers.
    /// </summary>
    public async Task<Dictionary<string, int>> GetAuthorBookCountsAsync(IReadOnlyCollection<string> authorNames)
    {
        if (authorNames.Count == 0)
        {
            return new Dictionary<string, int>();
        }

        var rows = await _db.Audiobooks
            .AsNoTracking()
            .Where(a => a.Authors.Any(p => authorNames.Contains(p.Name)))
            .SelectMany(a => a.Authors)
            .Where(p => authorNames.Contains(p.Name))
            .GroupBy(p => p.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToListAsync();

        return rows.ToDictionary(r => r.Name, r => r.Count, StringComparer.Ordinal);
    }

    public Task<Person?> GetByIdAsync(long id) =>
        _db.Persons.FirstOrDefaultAsync(p => p.Id == id);

    public async Task SetAuthorMatchAsync(long personId, string? matchedSourceName, string? sourceId, string? sourceUrl)
    {
        var person = await _db.Persons.FirstOrDefaultAsync(p => p.Id == personId)
            ?? throw new KeyNotFoundException($"Person {personId} not found");

        person.MatchedSourceName = matchedSourceName;
        person.MatchedSourceId = sourceId;
        person.MatchedSourceUrl = sourceUrl;
        await _db.SaveChangesAsync();
    }

    public async Task SetLastRefreshedAtAsync(long personId, DateTime at)
    {
        var person = await _db.Persons.FirstOrDefaultAsync(p => p.Id == personId)
            ?? throw new KeyNotFoundException($"Person {personId} not found");

        person.LastRefreshedAt = at;
        await _db.SaveChangesAsync();
    }

    public async Task<List<Person>> GetMatchedAuthorsAsync()
    {
        return await _db.Persons
            .AsNoTracking()
            .Where(p => p.MatchedSourceId != null && p.MatchedSourceId != "")
            .ToListAsync();
    }
}
