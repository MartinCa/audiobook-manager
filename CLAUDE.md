# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Audiobook Manager is a full-stack web application that organizes m4b audiobook files from an import directory into a structured library, with metadata scraping and Audiobookshelf integration. Backend is ASP.NET Core 8.0, frontend is Vue 3 + Vuetify 3 (TypeScript), database is SQLite via EF Core, deployed via Docker.

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

## Architecture

### Backend — Layered .NET Solution (`AudiobookManager/`)

- **Api** — ASP.NET Core Web API. Controllers in `Controllers/`, SignalR hub (`OrganizeHub` at `/hubs/organize`), background worker (`OrganizeWorker`) for queue processing.
- **Services** — Business logic layer.
- **Database** — EF Core context, models, and migrations. SQLite with snake_case naming convention.
- **Domain** — Domain models shared across layers (Audiobook, Person, Genre, SeriesMapping, QueuedOrganizeTask).
- **FileManager** — File I/O operations for audiobook organization.
- **Scraping** — Metadata scraping (AngleSharp). Goodreads integration for book metadata.
- **Settings** — Configuration classes. Key env vars: `AudiobookImportPath`, `AudiobookLibraryPath`, `DbLocation`.
- **HubClient** — SignalR client library.
- **Test** — MSTest unit tests with Moq for mocking.

### Frontend (`client/`)

- **Framework**: Vue 3 + Vuetify 3 + Vue Router + TypeScript
- **Services** (`src/services/`): API layer using Axios. `BaseHttpService.ts` is the shared HTTP wrapper.
- **Real-time**: SignalR via `@quangdao/vue-signalr` for progress updates during organization tasks.
- **Components** (`src/components/`): `BookOrganize.vue` is the main organization workflow component.

### Communication

Backend exposes REST API + SignalR hub. Frontend connects to both. SignalR messages: `ProgressUpdate`, `QueueError`.

### CI/CD

- `docker-image.yml` — Build validation on push/PR to main
- `prettier_format_ci.yml` — Frontend formatting validation
- `publish_to_dockerhub.yml` / `publish_to_github.yml` — Release publishing to Docker Hub and GHCR

## Key Configuration

- Swagger UI available in development at `/swagger/index.html`
- Vite dev server on port 3000, API on port 5271
- Audio metadata handled via `z440.atl.core` library
- HTTP resilience via Polly
