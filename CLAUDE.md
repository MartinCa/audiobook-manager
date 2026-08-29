# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Audiobook Manager is a full-stack web application that organizes m4b audiobook files from an import directory into a structured library, with metadata scraping and Audiobookshelf integration. Backend is ASP.NET Core (net10.0), frontend is Vue 3 + Vuetify 3 (TypeScript), database is SQLite via EF Core, deployed via Docker.

## Common Commands

### Backend (.NET)

```bash
# Build
cd AudiobookManager && dotnet build

# Run API (dev mode, http://localhost:5271)
cd AudiobookManager/AudiobookManager.Api && dotnet run

# Run tests
cd AudiobookManager && dotnet test

# Run a single test
cd AudiobookManager && dotnet test --filter "FullyQualifiedName~TestMethodName"

# Watch mode
cd AudiobookManager/AudiobookManager.Api && dotnet watch run
```

### Frontend (Vue/Node)

```bash
# Install dependencies
cd client && npm install

# Dev server (http://localhost:3000)
cd client && npm run dev

# Production build (includes vue-tsc type checking)
cd client && npm run build

# Run tests (Vitest)
cd client && npm test

# Format check (Prettier)
cd client && npm run format-check

# Auto-format
cd client && npm run format
```

### Docker

```bash
docker build -t audiobook-manager .
```

### EF Core Migrations

Startup project: `AudiobookManager.Api`, Default project: `AudiobookManager.Database`. Migrations auto-run on startup via `context.Database.Migrate()`.

```bash
# Generate a migration (run from AudiobookManager/)
dotnet ef migrations add <MigrationName> --startup-project AudiobookManager.Api --project AudiobookManager.Database
```

## Architecture

### Backend — Layered .NET Solution (`AudiobookManager/`)

- **Api** — ASP.NET Core Web API. Controllers in `Controllers/`, SignalR hub (`OrganizeHub` at `/hubs/organize`), background worker (`OrganizeWorker`) for queue processing. DTOs in `Dtos/`, SignalR message types in `Async/`.
- **Services** — Business logic layer. Each service has an interface + implementation pair registered in `DependencyInjection.cs`.
- **Database** — EF Core context (`DatabaseContext.cs`), models in `Models/`, entity mappings in `EntityMappings/`, repositories in `Repositories/`. SQLite with snake_case naming convention (`UseSnakeCaseNamingConvention()`). DI registration in `DependencyInjection.cs`.
- **Domain** — Domain models shared across layers (Audiobook, Person, AudiobookFileInfo, AudiobookImage). These are distinct from the Database models — the service layer maps between them.
- **FileManager** — File I/O operations: `AudiobookFileHandler` (static utility for path generation, file relocation, metadata/cover writing), `AudiobookTagHandler` (m4b tag parsing/writing via ATL library), `FileScanner` (directory scanning).
- **Scraping** — Metadata scraping. Scrapers: `GoodreadsScraper` (AngleSharp HTML scraping), `AudibleScraper`, `HardcoverScraper` (GraphQL API, requires an API key). See "Adding a metadata source scraper" below. **Hardcover GraphQL schema reference**: `docs.hardcover.app` is often unreachable from sandboxed/CI environments (egress-blocked) — instead fetch the authoritative schema SDL directly from `https://raw.githubusercontent.com/hardcoverapp/hardcover-docs/main/schema.graphql` (mirrors the docs site, no auth needed) before guessing field names for a new Hardcover query (e.g. `series`, `series_by_pk`, `book_series`). Check that file first rather than trial-and-error against the live API.

  All Hardcover HTTP traffic goes through the `"hardcover"` named client, whose `HardcoverRateLimitingHandler` (`Scraping/RateLimiting/`) enforces the limits for every call site: a shared singleton `TokenBucketRateLimiter` queues requests for the burst/per-minute budget (invariant: bucket capacity + per-minute refill must stay at or under the API's hard 60/min ceiling — validated at startup, throws on misconfiguration), and a persisted per-UTC-day counter (`hardcover_request_quota` table) is incremented and checked *before* each request, throwing `HardcoverDailyLimitExceededException` when the daily budget is spent. `HardcoverRetryHandler` sits *outside* the rate limiter so every retry re-acquires a token instead of bypassing it.

  **Hardcover API limitations** (see [docs.hardcover.app/api/getting-started/#limitations](https://docs.hardcover.app/api/getting-started/#limitations) — that page is often egress-blocked in sandboxed/CI environments, so this is a durable summary, not a substitute for checking the live page when reachable): these are *query-shape* restrictions, separate from and enforced independently of the request-count/rate limits above.
  - Pattern-matching filter operators are disabled server-side and return HTTP 403 (`"... operations are not permitted on this server"`) even though they still appear in the published schema types: `_like`, `_nlike`, `_ilike`, `_niregex`, `_nregex`, `_iregex`, `_regex`, `_nsimilar`, `_similar`. Never build a `where: { field: { _ilike: ... } }`-style query against Hardcover — fuzzy/partial/typo-tolerant name matching (e.g. series search) must go through the Typesense-backed `search()` query instead (`query_type` values documented at the URL above; see `HardcoverScraper.Search()`/`SearchSeries()` for the pattern).
  - General queries time out at 30 seconds; `search()` queries time out at 2 seconds.
  - Queries must not run in a browser — the API key has to stay server-side.
- **Settings** — `AudiobookManagerSettings` with key env vars: `AudiobookImportPath`, `AudiobookLibraryPath`, `DbLocation`. Hardcover rate limiting is configured here too: `HardcoverBurstLimit` (5), `HardcoverPerMinuteLimit` (55) and `HardcoverDailyRequestLimit` (5000), all sized under Hardcover's Free-plan limits (burst 10, 60/min, 5000/day).
- **HubClient** — SignalR client library. Implements `IOrganize` interface — must be updated when new SignalR events are added.
- **Test** — MSTest unit tests with Moq for mocking.

### Vuetify MCP

When working on frontend Vuetify components, use the [Vuetify MCP server](https://github.com/vuetifyjs/mcp/) whenever possible. It provides direct access to Vuetify component APIs (props, events, slots, methods) and documentation, enabling accurate AI-assisted development.

Connect via the hosted server at `https://mcp.vuetifyjs.com/mcp` or run locally with `npx -y @vuetify/mcp`.

### Frontend (`client/`)

- **Framework**: Vue 3 + Vuetify 3 + Vue Router + TypeScript
- **Services** (`src/services/`): API layer using Axios. `BaseHttpService.ts` is the shared HTTP wrapper with `getData`, `postData`, `putData`, `delete` methods. Each service is a singleton class instance export.
- **Real-time**: SignalR via `@quangdao/vue-signalr`. Message types defined in `src/signalr/` as TypeScript interfaces. Listeners use typed `HubEventToken<T>` tokens.
- **Components** (`src/components/`): `BookOrganize.vue` (organization workflow), `BookLibrary.vue` (library management + scan), `LibraryConsistency.vue` (consistency checking). Library sub-views in `components/library/`.
- **Routing**: Hash-based routing configured in `main.ts`. Navigation links defined in `App.vue`.
- **Types** (`src/types/`): TypeScript interfaces for API response shapes.

### Communication

Backend exposes REST API + SignalR hub. Frontend connects to both. SignalR messages: `ProgressUpdate`, `QueueError`, `LibraryScanProgress`, `LibraryScanComplete`, `ConsistencyCheckProgress`, `ConsistencyCheckComplete`.

### CI/CD

- `docker-image.yml` — Build validation on push/PR to main
- `prettier_format_ci.yml` — Frontend formatting validation
- `publish_to_dockerhub.yml` / `publish_to_github.yml` — Release publishing to Docker Hub and GHCR

## Key Patterns

### Fire-and-forget async with SignalR progress

Long-running operations (library scan, consistency check) use this pattern in controllers:

1. Controller endpoint returns `Ok()` immediately
2. `Task.Run` spawns background work with `_serviceScopeFactory.CreateScope()` for DI
3. Service accepts a `Func<..., Task> progressAction` callback
4. Controller wires the callback to `_organizeHub.Clients.All.SomeEvent(...)` for real-time updates
5. A completion event is sent when done (also on error, with zeroed values)

### Domain vs Database models

`Domain.Audiobook` is the rich model used by services/file operations (has `FileInfo`, `Cover` image data, nullable `Year`). `Database.Models.Audiobook` is the EF entity (has flat file path columns, non-nullable `Year`). `AudiobookService.FromDb()` maps between them.

### Repository pattern

Each DB entity gets an `IRepository` + `Repository` pair in `Database/Repositories/`, registered as scoped in `Database/DependencyInjection.cs`. Entity relationships are configured in `EntityMappings/` classes implementing `IEntityTypeConfiguration<T>`.

### File path generation

`AudiobookFileHandler.GenerateRelativeAudiobookPath()` builds: `Author / [Series /] [BookNN - ] Year - BookName / filename.m4b`. All path parts are sanitized via `GetSafeFileName()` and `GetSafeCompletePath()`. The library root is prepended by the service layer using settings.

### Comparing file paths — always OS-aware, never raw strings

**Invariant: never compare two file paths with `==`, `!=`, `string.Equals`, or `StartsWith`, and
never key a path-based `HashSet`/`Dictionary` with the default comparer.** Path comparison has
bitten this codebase repeatedly (the duplicate-detection self-match, the sidecar cleanup, the
allowed-base check, the library scan's known-path set), because two things are easy to forget:
paths need normalizing (`.`/`..`/`//`/mixed separators), and case-sensitivity is a property of the
*file system*, not of the string. Use the helpers on `AudiobookFileHandler`:

- `PathsEqual(a, b)` — do these denote the same file?
- `PathStartsWith(path, prefix)` — is `path` inside `prefix`? This checks the **path boundary**, so
  `/data/library-backup` is correctly *not* inside `/data/library`. A bare `StartsWith` here once
  let `FileService.DeleteDirectory` — which deletes recursively — reach a sibling directory.
- `PathComparison` / `PathComparer` — the `StringComparison`/`StringComparer` to hand to anything
  that compares or hashes paths itself (`ToHashSet`, `GroupBy`, `Distinct`, `OrderBy`).

When a repository returns a set of paths for membership testing, it takes the comparer from the
caller (`GetAllFilePathsAsync(AudiobookFileHandler.PathComparer)`) rather than defaulting.

### Search and type-ahead matching must be accent-insensitive

**Invariant: every text search, filter, or autocomplete path a user types into must match
regardless of diacritics** — typing `Rene` has to find `René`, and typing `René` has to find
`Rene`. Neither SQLite's default BINARY collation (used by every `LIKE`/`Contains` in this
codebase — there is no ICU/custom collation registered) nor plain JavaScript string comparison
folds accents on its own, so this doesn't happen for free; it was audited and found completely
missing (backend and frontend) before the fix that added the helpers below. When adding a new
search/filter/autocomplete code path, route it through the existing accent-folding helper for
its layer rather than comparing raw strings:

- **Backend SQL search** (`AudiobookRepository.SearchAsync`/`SearchSeriesAsync`,
  `PersonRepository.SearchAuthorSummariesAsync`, `DiscoveredAudiobookRepository.GetPaginatedAsync`)
  — wrap both the column and the query pattern in `AudiobookManager.Database.Search.AccentFolding`:
  `EF.Functions.Like(AccentFolding.Fold(column), $"%{AccentFolding.FoldPlain(query)}%")`.
  `Fold(string?)` is a marker method EF Core translates to a call to the `fold_accents` SQLite
  scalar function (registered per-connection by `AccentFoldingConnectionInterceptor`, since the
  connection object doesn't exist yet inside `DbContext.OnConfiguring` — see that class's
  comment for why a connection interceptor is the correct hook and not `Database.GetDbConnection()`
  directly). `FoldPlain(string?)` is the real CLR implementation, used both as that SQL function's
  body and to fold the C#-side query string before it goes into the LIKE pattern.
- **Frontend filtering/autocomplete** (list-narrowing `computed`s, `narrowByQuery`,
  `normalizeForMatch`/`isNearMatch` in `client/src/helpers/similarValueMatcher.ts`) — wrap both
  sides of the comparison in `foldAccents` from that same file
  (`value.normalize("NFD").replace(/[\u0300-\u036f]/g, "")`, i.e. Unicode NFD decomposition
  followed by stripping combining marks).

This is orthogonal to `NameNormalizer`/`SimilarityGrouper` (the similar-author/series duplicate
*detection* feature) — that normalizer is intentionally accent-naive today (`"René"` and `"Rene"`
cluster only if close enough by edit distance, not because they're treated as equal), since it
serves a different purpose (flagging likely-duplicate *stored* values) with its own thresholds
tuned around that behavior. Don't assume fixing one fixes the other.

### Tag round-trip normalization must mirror the tag writer

`AudiobookService` verifies that saved tags read back as requested before it relocates the file or
touches the DB, comparing via `TagConsistencyChecker.FindMismatches`. **Any normalization the tag
writer applies has to be mirrored in the checker**, or a book becomes permanently un-saveable: the
save throws every time, with a misleading "non-contiguous QuickTime chapters" message. Known
normalizations: `GetStringFromListOfPersons` de-duplicates names (so `["King", "King"]` writes as
`"King"`), and an empty genre tag reads back as no genres at all.

Relatedly, **splitting a free-text tag field never uses a bare `Split`**: `"".Split("/")` yields
`[""]`, not `[]`, and that blank entry used to be persisted as a real `Person`/`Genre` row with an
empty name that every untagged book linked to. Use `AudiobookTagHandler.ParseGenresFromString` /
`ParsePersonsFromString` on the backend and the `splitList` helper in
`helpers/organizeAudiobookInput.ts` on the frontend; both use
`StringSplitOptions.RemoveEmptyEntries | TrimEntries` semantics. `AudiobookController.MapToDomain`
also scrubs blank names off incoming DTOs, since the client splits these fields itself.

### Bulk EF operations and the change tracker

`ExecuteDeleteAsync`/`ExecuteUpdateAsync` are strongly preferred over `RemoveRange(dbSet)` +
`SaveChanges` for clearing or bulk-updating a table — the latter fetches and tracks every row just
to issue one statement per id. But they **bypass the change tracker**, which has two consequences:

1. Rows this context already loaded stay in the identity map. SQLite reuses deleted rowids, so a
   *newly inserted* row can be handed an id a ghost still holds, and EF resolves it back to the
   stale entity. After a set-based delete, detach the affected entries (see
   `ConsistencyIssueRepository.DetachTracked`), and read with `AsNoTracking()`.
2. **Do not use them where the entity has an inverse navigation the caller holds.**
   `SeriesRepository.ReplaceExpectedBooksAsync` deliberately stays on the tracked
   `RemoveRange` path, because callers hold a tracked `Series` whose `ExpectedBooks` collection EF
   keeps fixed up — a set-based delete would leave the deleted rows in that collection.

**A read-then-insert on a uniquely-indexed column needs the same treatment.** Repositories that
resolve "get this row or create it" span an `await` on a request-scoped context, so two callers
can both find the row missing and both insert it. That is a live race, not a theoretical one -
organizes run alongside interactive saves and bulk operations. `PersonRepository.GetOrCreatePersons`
and `SeriesRepository.UpsertByNameAsync` (which backs both `UpsertSeriesAsync` and
`SetIncludeOmnibusEditionsAsync`) catch it via `SqliteErrors.IsUniqueViolation`, detach the entity
they added, re-read, and apply their change to the winner's row. Without it the loser failed the
whole request with a raw `UNIQUE constraint failed` 500 - reproduced live with four concurrent
first-time writes for one series, two of which 500'd.

A read-modify-write across `await` is not safe for a counter either: `HardcoverQuotaRepository`
does its compare-and-increment in a single `ExecuteUpdateAsync` statement, because Hardcover
requests genuinely run concurrently (`SearchMultiple` fans out; the retry handler re-enters) and a
C#-side increment loses updates and overruns the daily budget.

### Long-running and file-mutating endpoints need a concurrency gate

Anything that rewrites m4b tags or relocates files must not be able to run twice concurrently for
the same book. Use `BackgroundOperationRunner` (a process-static `SemaphoreSlim` plus
`IOperationStatusRegistry`) for whole-library operations, and a per-entity gate for per-book work:
`AudiobookController.UpdateAudiobook` keys a `ConcurrentDictionary<long, SemaphoreSlim>` by
audiobook id and returns `409 Conflict` when a save for that book is already in flight. Without it,
two saves both read the same pre-move path, the first relocates the file, and the second writes
tags to a path that no longer exists.

Resolve DI services for background work from `_serviceScopeFactory.CreateScope()`, never from the
controller's own request-scoped instances. At startup, use `app.Services.CreateScope()` — never
`builder.Services.BuildServiceProvider()`, which builds a second, never-disposed container whose
singletons are not the ones the app runs with (the compiler flags this as `ASP0000`).

### Reading only what the response needs

Repository methods project in SQL rather than materializing entity graphs the caller then reduces.
The recurring mistake is `Include`-ing a collection to read `.Count` off it — `GET /browse/authors`
once loaded every audiobook row, `Description` blobs included, once per author. Prefer a projection
type (`AuthorSummaryRow`, `AuthorBookRef`) or a scalar query (`GetCoverFilePathAsync`,
`GetSeriesNamesAsync`) over `Include` + in-memory reduction, and add `AsNoTracking()` to every
read-only query.

Two caveats when moving work into SQL:
- **Moving an `OrderBy` into SQL changes the collation.** SQLite's default is BINARY, i.e. by
  code point: `"Zadie"` sorts before `"alice"`, and every accented name lands after `"Z"`. That
  is the wrong order for anything a person reads — the autocomplete name lists
  (`GetAuthorNamesAsync`, `GetSeriesNamesAsync`) and the author-detail sections deliberately
  project in SQL but `Sort`/`OrderBy` in memory with `StringComparer.InvariantCulture`. Push the
  *projection* down; keep presentation ordering in .NET unless the query is also paged (a paged
  query has to order in SQL — see below — so it gets BINARY order and there is no way around it).
- **Blocking work does not belong on a request thread.** `DetectIssuesForAudiobook` is
  synchronous (an ATL parse of the whole m4b plus several file reads), so the single-book
  recheck wraps it in `Task.Run`. The full check needs no wrapper — `BackgroundOperationRunner`
  already runs it on the thread pool.

Two more rules for query shape:
- **A paged query needs a total order.** `OrderBy(a => a.BookName)` is not one — books sharing a
  title have an undefined relative order, so the same row can appear on two pages while another is
  skipped, and with `AsSplitQuery()` the `Skip`/`Take` runs in *each* query, so they can disagree
  about the page's contents. Always add `.ThenBy(a => a.Id)`.
- **`ParseAudiobook(fileInfo, includeCoverData: false)`** for any caller that only needs to know
  whether a cover exists (the consistency check, the save round-trip verification, the library
  scan). Encoding the picture allocates the bytes plus a base64 string ~1.4x their size, per book.

### Free-text values are addressed in the query string, never in a path segment

**Invariant: a series name (or any other raw m4b tag value) must never be a route parameter.**
Series are addressed by their free-text name rather than a catalog id - an unmatched series
exists only as a value on audiobooks and has no catalog row yet - and that value can contain any
character a tag can. A `/` in it is fatal in a path: ASP.NET Core leaves `%2F` percent-encoded
rather than decoding it into a segment separator, so the action receives the literal `%2F` and
every lookup misses. A series named `Sword Art Online / Progressive` was listed on the overview
page and then 404'd the moment it was opened, with no way to match, refresh or ignore anything
in it. `SeriesController` and `BrowseController.GetSeriesBooks` take `[FromQuery] string
seriesName` against fixed action paths (`api/series/detail`, `api/series/match`,
`api/browse/series`, ...); `SeriesControllerTests` has a reflection guard that fails if any route
template on either controller reintroduces `{seriesName}`.

Vue Router is *not* affected - it decodes params after matching, so `/library/series/:seriesName`
handles such a name correctly - which is why the series was clickable but its API calls were not.

### Metadata sidecar files

Alongside each m4b, `WriteMetadata()` creates `desc.txt` (description), `reader.txt` (narrators)
and `metadata.opf`. `WriteCover()` extracts embedded cover art to `cover.jpg` or `cover.png`.

**These sidecars are owned by this application, so writing them is also what removes them.** A
field that is now empty deletes its sidecar rather than leaving the previous one in place, and
writing `cover.jpg` deletes any `cover.png` beside it (and vice versa). This matters because
Audiobookshelf reads `desc.txt` in preference to the m4b's own Description tag: a leftover file
keeps serving metadata the book no longer has. The same rule applies to detection —
`LibraryConsistencyService` reports a sidecar that exists while its tag is empty as
`IncorrectDescTxt`/`IncorrectReaderTxt`, so it is visible and resolvable rather than silently
skipped (the "tag is empty, nothing to compare" branch used to skip the file entirely, which is
how stale sidecars survived every save and every consistency run).

### Similar author/series detection & bulk alignment

Author names and series values are free text, so the same real-world value can end up recorded with small textual differences (`J.K. Rowling` vs `JK Rowling`, `Fantasy & Adventure` vs `Fantasy and Adventure`). This feature is stateless/computed — there is no persisted "issue" table like `ConsistencyIssue`; groups are detected fresh on every request.

- **Fuzzy matching** — `AudiobookManager.Services/Similarity/`: `NameNormalizer` (comparison-only normalization — lowercase, strip punctuation, merge initials — never written back to the DB), `LevenshteinDistance` (standalone edit-distance, no NuGet dependency), and `SimilarityGrouper` (clusters distinct values via normalized-equality or a length-scaled edit-distance threshold, using union-find with length-bucketed blocking). Thresholds live on `AudiobookManagerSettings`.
- **Detection & alignment** — `ISimilarValueService`/`SimilarValueService`: `DetectSimilarAuthorsAsync()`/`DetectSimilarSeriesAsync()` read distinct values and cluster them; `AlignAuthorsAsync()`/`AlignSeriesAsync()` bulk-rewrite a chosen target value across all affected books. Alignment is **per-book**, wrapped in try/catch so one failure (e.g. a generated-path collision) never aborts the rest of the batch, and reports `(processed, total, succeeded, failed)` via a progress callback — mirroring `LibraryConsistencyService`'s bulk-resolve pattern.
- **API** — `SimilarValuesController` (`api/similar-values`): `GET similar-authors`/`similar-series` (synchronous — DB read + in-memory clustering), `POST align` (fire-and-forget with SignalR progress, mirroring `ConsistencyController`), and `GET author-names`/`series-names` (cheap flat lists for the entry-time autocomplete below).
- **UI** — `SimilarValues.vue`: review each group, pick a target value (existing candidate or free text), confirm, watch live progress.
- **Entry-time duplicate prevention** — the add/edit book form (`BookDetail.vue`) fetches the flat name lists (`SimilarValueService.ts` on the frontend, with a 5-minute in-memory cache) to power an autocomplete dropdown on the Author/Series fields while typing, and a "similar existing entries" hint with click-to-use after metadata is filled from a scrape. This client-side matching is a separate, simpler JS implementation (`helpers/similarValueMatcher.ts`) — it's advisory UI only and does not need to match the backend `SimilarityGrouper` byte-for-byte.

**Binding invariant: no DB-only field updates for Author/Series/SeriesPart/Year/BookName.** Any code path that changes `Author`, `Series`, `SeriesPart`, `Year`, or `BookName` on a library audiobook — a single edit, a bulk operation, anything — must go through `AudiobookService.UpdateAudiobook` (directly, or per-book in a loop for bulk operations like `AlignAuthorsAsync`/`AlignSeriesAsync` above). Never write those fields to the database directly. This is required because `UpdateAudiobook` always rewrites the m4b tags, always recomputes the library path from the *entire* object and relocates the file (cleaning up stale sidecars) whenever that path differs from the current one, and always rewrites `desc.txt`/`reader.txt`/cover sidecars regardless of whether a relocation happened. A DB-only update would silently desync the file on disk from the database record. Note `LibraryConsistencyService.ResolveWrongFilePath` is a narrower, special-cased path-only repair (it assumes the m4b tags are already correct and only needs to move the file) — it is not a template for general field edits.

### Adding a metadata source scraper

Adding a new source (or changing an existing one's name/availability) requires touching exactly **one file** — the scraper itself — and nothing else, front or back end:

1. Implement `IScraper` (`AudiobookManager.Scraping/Scrapers/IScraper.cs`) in a new class under `AudiobookManager.Scraping/Scrapers/`. Set `SourceName`, `IsSource()`, `SupportsUrl()`, `Search()`, `GetBookDetails()`, and optionally `RequiresApiKey`/`IsApiKeyConfigured` if it needs a key (see `HardcoverScraper` for the pattern).
2. That's it. DI registration is reflection-based (`AudiobookManager.Scraping/DependencyInjection.cs` registers every non-abstract `IScraper` in the assembly automatically) — no manual wiring.
3. `ScrapingService.GetSearchServiceInfo()` (`GET /api/metadata-search/services`) automatically includes the new scraper, and `ScrapingService.Search`/`SearchMultiple`/`GetBookDetails` automatically tags results with the scraper's `SourceName`.
4. The frontend's source picker and the remembered-source-selection composable (`useSelectedSearchSources.ts`) derive their list of sources from `GET /metadata-search/services` live — **neither hardcodes source names**, and neither should be edited when a scraper is added, removed, or renamed. Do not reintroduce a hardcoded fallback source list on the frontend (one existed in `BookSearchDialog.vue` and was removed for exactly this reason — it silently drifted from the real backend list).

`BookSearchDialog.vue`'s single search field doubles as "add by URL": on submit, an absolute `http(s)` value goes straight to `MetadataSearchService.getBookDetails()` (skipping source selection entirely) instead of the multi-source search, so pasting a book URL from any configured source adds it directly. There is no separate "Add by URL" dialog/button.

## Frontend Patterns

### Async list loads need a request-sequence guard

Any loader whose result overwrites shared state (`loadBooks`, `loadDiscoveredBooks`, `runSearch`)
must ignore its own stale responses. Debounced search and pagination overlap constantly, and
without this a slow `"har"` landing after a fast `"harry"` renders the wrong page:

```ts
let loadRequestId = 0;

const load = async () => {
  const requestId = ++loadRequestId;
  const result = await Service.fetch(...);
  if (requestId !== loadRequestId) return;   // a newer load won
  items.value = result.items;
};
```

### Cancel debounced callbacks on unmount

Every `debounce(...)` in a component needs a matching `onUnmounted(() => fn.cancel())`, or it
fires after the component is gone — mutating dead refs and issuing a request nobody reads. The
same pairing rule as SignalR listeners, which is what `useSignalREvent`/`useSignalRReconnected`
exist to enforce; prefer those over raw `on`/`off`.

### Never key a mutating list by array index

`v-for` over a list that is mutated at runtime must be keyed by a stable identity
(`:key="book.fullPath"`), and any "which row is open/selected" state must hold that same
identifier — not an index. `BookList.vue` and `DiscoveredAudiobooks.vue` both track the open
expansion panel by path (`:value="book.fullPath"`), because removing a row above the open one
used to silently re-point the panel at whichever book shifted into that slot, with Vue reusing
the already-open form for a different file.

### Optimistic UI must not outrun what the server reported

A bulk operation returns `{ resolved, failed }`. Removing everything it touched from the list
regardless of `failed` hides genuinely unresolved items until the next full check. Re-read the
authoritative list in a `finally` instead of reproducing the server's resolution rules
client-side (`LibraryConsistency.vue`'s `bulkResolve`/`resolveSelected`).

### Keep O(n) work out of the template

A plain function called from a template re-runs on every render, for every item. Where it scans a
collection, hoist it into a `computed` that produces the whole lookup in one pass —
`LibraryConsistency.vue`'s `groupSelectionState` replaced per-group helpers that made ticking one
checkbox re-scan every group's full issue array four or more times.

### Send only what the endpoint reads

`AudiobookService.ts` keeps `toDto` (the full save payload, cover included) separate from
`toPathPreviewDto` (only the fields `GenerateRelativeAudiobookPath` actually reads). The preview
endpoints are called from a debounced keystroke watcher, so anything extra — the cover's base64
payload, but also a multi-kilobyte description — is re-uploaded on every edit for a value the
server ignores. Watchers that trigger those calls must also avoid *reading* the cover fields, or
they track them as reactive dependencies and retrigger on cover edits.

## Key Configuration

- Swagger UI available in development at `/swagger/index.html`
- Vite dev server on port 3000, API on port 5271
- Audio metadata handled via `z440.atl.core` library (ATL)
- HTTP resilience via Polly
- **TypeScript pinned to 6.x** — do not upgrade to TypeScript 7 yet. TS 7.0 dropped the Compiler/AST API that `vue-tsc` relies on, so `vue-tsc` (and therefore `npm run build`) breaks on it. Official support is blocked on TS 7.1's plugin interface (see [vuejs/language-tools#5381](https://github.com/vuejs/language-tools/issues/5381)); an interim third-party shim (`typescript-native-bridge`) exists but isn't worth adopting for this project. Re-check once `vue-tsc` ships native TS 7.1 support.

## Testing Policy

**Every new feature ships with tests, and every bug fix ships with a regression test.** A
change is not complete until the tests covering it exist and pass.

- **New features** — cover the behavior the feature adds, including its failure and edge cases
  (empty/null inputs, error paths, permission/limit boundaries), not just the happy path. A
  new service gets a test class; a new endpoint gets a controller test; a new helper,
  composable, or component gets a `*.test.ts`.
- **Bug fixes** — first write a test that fails against the unfixed code and passes with the
  fix, so the specific bug can never silently return. Reference the failure in the test name
  (e.g. `..._DoesNotResurrectStaleSidecarsOnRelocation`).
- **Invariants** — behavior CLAUDE.md calls out as an invariant (the Author/Series/SeriesPart/
  Year/BookName binding rule, "no hardcoded source list on the frontend", Hardcover's
  disabled pattern-matching operators) deserves an explicit regression guard, since the cost
  of a silent regression there is high.

Where tests live:
- **Backend** — MSTest + Moq in `AudiobookManager/AudiobookManager.Test/`, mirroring the source
  layout (`Services/`, `Controllers/`, `FileManager/`, `Repositories/`, `Scraping/`). Test
  fixtures go in a `TestData/` folder next to the tests that use them.
- **Frontend** — Vitest, named `*.test.ts` (**not** `.spec.ts`), colocated beside the file under
  test. Vuetify component tests mount with the plugin registered — see `client/vitest.setup.ts`
  for the jsdom polyfills (`ResizeObserver`, `visualViewport`) Vuetify overlays need.

Writing tests that actually catch regressions:
- **Assert on the exact value, not a loose substring.** `toContain("Searching: A, B")` still
  passes when a third item is appended; `toEqual(["A", "B"])` does not. If unsure a test can
  fail, break the production code and confirm it goes red.
- **The test name must match what it asserts.** A test named "returns true for …" that asserts
  `false` misleads every future reader.
- **Never wait on a fixed `Task.Delay`/`setTimeout` for background work to settle** — poll the
  real condition with a timeout instead (see `AudiobookManager.Test/Controllers/OperationGate.cs`).
  Fixed sleeps are flaky under CI contention and slow the suite down.
- **Prove a regression test actually fails without the fix.** Revert the production change (or
  break it), confirm the new test goes red, then restore. Several "regression" tests in this
  repo's history asserted behavior that held before the fix too. If a test cannot be made to
  fail, say so in the test's own comment rather than labelling it a regression guard — a fix can
  be correct and defensive without being a live bug (see the sidecar-cleanup note in
  `AudiobookService.RelocateIfPathChangedAsync`).
- **Watch for tests that pass vacuously.** A setup that silently no-ops (an event whose id never
  matches, a mock that is never hit) makes the assertions meaningless. Assert on the resulting
  state, not just the field you set — e.g. also assert the list actually changed length.
- **Widen mock setups when adding an optional parameter.** Moq matches
  `ParseAudiobook(It.IsAny<FileInfo>())` as `(fileInfo, true)`, so a caller passing `false` gets
  a null result instead of the configured one. Use `It.IsAny<bool>()` and, for callbacks, the
  full arity: `.Returns((FileInfo fi, bool _) => ...)`.
- **Give process-static state a distinct key per test.** The per-audiobook save gates and the
  `BackgroundOperationRunner` semaphores outlive a single test; use a unique id per test and poll
  the real release condition (see `OperationGate`) rather than assuming the background task's
  `finally` has already run.
- **Keep real sleeps out of the suite.** Collapse retry/backoff waits via the code's own
  levers (e.g. a `Retry-After` header) rather than letting a test sit through an exponential
  backoff.

## Verification Checklist

After making changes, run all five — including when a change looks backend- or
frontend-only, since edits to shared files (e.g. `client/src/signalr/hub.ts`) can break
the other side's tests too:

1. `cd AudiobookManager && dotnet build` — 0 errors
2. `cd AudiobookManager && dotnet test` — all pass
3. `cd client && npm run build` — type-check + build
4. `cd client && npm test` — Vitest unit tests, all pass
5. `cd client && npm run format-check` — Prettier formatting
