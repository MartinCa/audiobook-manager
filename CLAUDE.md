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

### Metadata sidecar files

Alongside each m4b, `WriteMetadata()` creates `desc.txt` (description) and `reader.txt` (narrators). `WriteCover()` extracts embedded cover art to `cover.jpg` or `cover.png`.

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
