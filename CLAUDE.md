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
- **Scraping** — Metadata scraping (AngleSharp). Goodreads and Audible integration for book metadata.
- **Settings** — `AudiobookManagerSettings` with key env vars: `AudiobookImportPath`, `AudiobookLibraryPath`, `DbLocation`.
- **HubClient** — SignalR client library. Implements `IOrganize` interface — must be updated when new SignalR events are added.
- **Test** — MSTest unit tests with Moq for mocking.

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

## Key Configuration

- Swagger UI available in development at `/swagger/index.html`
- Vite dev server on port 3000, API on port 5271
- Audio metadata handled via `z440.atl.core` library (ATL)
- HTTP resilience via Polly

## Verification Checklist

After making changes, run all four:

1. `cd AudiobookManager && dotnet build` — 0 errors
2. `cd AudiobookManager && dotnet test` — all pass
3. `cd client && npm run build` — type-check + build
4. `cd client && npm run format-check` — Prettier formatting
