# DESIGN.md

Frontend conventions for this repository. These rules are binding for both humans and
coding agents. If a rule here conflicts with a suggestion from a model, a tutorial, or a
Stack Overflow answer, this file wins. If a rule here is genuinely wrong for this project,
change the file in the same PR as the code — do not silently deviate.

Agents: read this file in full before writing any UI code. Do not introduce a library that
is not listed in **Allowed dependencies** without asking first.

---

## 1. Stack

| Layer           | Choice                                                                                       | Not this                                                                       |
| --------------- | -------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------ |
| Language        | TypeScript, `strict: true`                                                                   | JavaScript, `any`                                                              |
| UI runtime      | React (function components + hooks only)                                                     | class components                                                               |
| Build / routing | Vite + TanStack Router (SPA) _or_ Next.js App Router (when SSR/SEO is genuinely needed)      | CRA, Webpack by hand                                                           |
| Components      | shadcn/ui                                                                                    | MUI, Ant, Chakra, Bootstrap                                                    |
| Primitives      | Base UI (shadcn default since July 2026). Pin the choice in `components.json`.               | mixing Radix and Base UI in one repo                                           |
| Styling         | Tailwind CSS + CSS variables for theme tokens                                                | CSS-in-JS, SCSS, inline `style={{}}`                                           |
| Icons           | `lucide-react`                                                                               | mixed icon sets                                                                |
| Server state    | TanStack Query                                                                               | fetch-into-`useState`, fetch-into-Zustand                                      |
| Client state    | `useState` → lifted props → Zustand (in that order)                                          | Redux, Context as a state manager                                              |
| Forms           | `react-hook-form` + `zod` (client-side validation only — the server validates independently) | hand-rolled validation                                                         |
| Tables          | TanStack Table (headless) + shadcn table primitives                                          | ag-grid, custom sort logic                                                     |
| Charts          | shadcn/ui Charts (Recharts)                                                                  | new charting lib per project                                                   |
| Dates           | `date-fns`                                                                                   | moment, hand-rolled parsing                                                    |
| Tests           | Vitest + Testing Library; Playwright only where a flow is worth it                           | Enzyme, snapshot-everything                                                    |
| Lint/format     | ESLint (typescript-eslint) + Prettier, both enforced in CI with `--max-warnings 0`           | per-file disables without a reason comment; a lint job that passes on warnings |

**Default to the SPA path.** Most projects here are internal tools behind auth on a
private network. They do not need SSR, RSC, or an SEO story, and the client/server
component boundary is a recurring source of agent mistakes. Reach for Next.js only when
there is a stated reason, and write that reason in the project README.

### Allowed dependencies

Anything in the table above, plus: `clsx`, `tailwind-merge`, `class-variance-authority`
(these come with shadcn), `sonner` for toasts, `cmdk` for command palettes.

Everything else requires a one-line justification in the PR description. Prefer writing
30 lines over adding a dependency for something small. Prefer a dependency over writing
300 lines of date/a11y/virtualization logic.

---

## 2. State: pick the right layer

Ask two questions in order.

**1. Does this data come from a server?**
Yes → TanStack Query. Always. Never copy query results into another store. Never manage
`loading` / `error` / refetch by hand. Derive from the query cache; invalidate on mutation.

**2. If not, how far does it need to travel?**

- One component → `useState` / `useReducer`.
- Parent and a couple of children → lift it, pass props. Prop drilling two levels is fine
  and is more legible to a reader (and to an agent) than an indirect store.
- Genuinely cross-cutting, or read by components with no common ancestor → **Zustand**.

Zustand is the right answer for: sidebar/panel open state, theme, selected items across a
split view, filter and view preferences, transient UI that survives navigation. It is the
wrong answer for anything the API owns.

React Context is for dependency injection (a client, a theme object, an auth session), not
for frequently-changing values.

### Zustand rules

- One store per domain concern, in `src/stores/<name>.ts`. Do not build a single app store.
- Export a typed hook and always select narrowly: `useUiStore((s) => s.sidebarOpen)`.
  Never `useUiStore()` with no selector — it subscribes the component to every change.
- Actions live inside the store next to the state they mutate. No action creators, no
  thunks, no middleware unless `persist` is needed for user preferences.
- Keep stores serializable. No class instances, no DOM nodes, no query clients.

---

## 3. shadcn/ui workflow

Components are **copied into this repo** and are therefore our code. That has consequences.

- Add components with the CLI, never by pasting from a blog: `pnpm dlx shadcn@latest add <name>`.
- Generated components live in `src/components/ui/` and are treated as **vendored**. Commit
  them in their own commit, separate from any customization.
- **Do not edit files in `src/components/ui/` to change appearance.** Restyle by overriding
  theme CSS variables, or by targeting `[data-slot="..."]` in the global stylesheet.
  Unmodified vendor files can be overwritten without thought, which is the whole point.
- If a component needs different _behaviour_, wrap it. Put the wrapper in
  `src/components/` (not `ui/`) and import the primitive from `ui/`.
- Updating: `shadcn diff` to see what changed upstream, `--dry-run` / `--view` to inspect
  before writing, then `add <name> --overwrite` and resolve with git. This is a deliberate,
  occasional chore — Renovate cannot do it. Run it when there is a reason to, not on a schedule.
- Use `shadcn docs <component>` to get current API surface rather than recalling props.

---

## 4. Structure

```
src/
  components/       app components, composed from ui/
    ui/             shadcn vendor components — do not hand-edit
  routes/           one file per route
  features/<name>/  feature-scoped components, hooks, and queries kept together
  stores/           zustand stores
  lib/
    api.ts          typed fetch client, single place that knows the base URL
    utils.ts        cn() and friends
  hooks/            shared hooks only; feature hooks live with the feature
```

- Colocate first. A component used by exactly one feature belongs in that feature folder.
  Promote to `src/components/` on the second consumer, not in anticipation of one.
- One component per file. File name matches the export. `PascalCase.tsx` for components,
  `camelCase.ts` for everything else.
- Named exports everywhere except route files.
- Import alias `@/` for `src/`. No `../../..`.

---

## 5. Styling

- Tailwind utilities in the markup. No separate stylesheet per component.
- **Never hardcode a colour.** Use the semantic theme tokens (`bg-background`,
  `text-muted-foreground`, `border-border`, `bg-destructive`). A hex value or a raw
  `bg-blue-500` in a component is a bug — it will break in dark mode and it breaks theming
  across projects. For health and status, use the shared status tokens
  (`text-status-ok`, `bg-status-warn`, `text-status-error`, `text-status-unknown`) rather
  than inventing a green per project; they come from
  `MartinCa/frontend-kit/theme` and are not part of the shadcn preset.
- Dark mode is a requirement, not a feature. Styling is free if the token rule is followed, but
  the app must still be wrapped in `MartinCa/frontend-kit/theme-provider`'s `<ThemeProvider>` —
  the `.dark` class in `theme.css` only applies when something toggles it. Without the provider
  the app always renders light, regardless of the system preference.
- Spacing uses the Tailwind scale. No arbitrary values (`p-[13px]`) without a comment.
- Compose conditional classes with `cn()`. Never build class strings with template literals
  and ternaries inline.
- Variants belong in `cva`, not in a pile of boolean props.
- Responsive down to a phone. If a table cannot work at 375px, show a card list instead.

---

## 6. Quality floor

Non-negotiable, and not worth discussing in review because it is written here:

- Keyboard reachable, with a visible focus ring. Never remove the outline without a
  replacement.
- Real semantics: `<button>` for actions, `<a>` for navigation, labelled inputs. A `<div>`
  with an `onClick` is a defect.
- `prefers-reduced-motion` respected.
- Every async surface has three defined states: loading (skeleton, not a spinner-only
  screen), empty (with an action to take), and error (what failed and what to do next).
- Error text says what happened and how to fix it. It does not apologize and it is not vague.
- Button labels are verbs and stay consistent through a flow — "Publish" produces "Published".

---

## 7. Backend contract

This stack is backend-agnostic. It talks HTTP and JSON and does not care whether the server
is ASP.NET Core, FastAPI, Flask, or a Go binary. The rules below keep it that way.

- **OpenAPI is the contract.** The backend publishes a spec; the frontend generates types
  from it into `src/lib/api-types.ts` and never hand-writes response interfaces. Regenerate
  as a checked-in build step so the diff is visible in review. FastAPI produces a spec from
  its models automatically; ASP.NET Core produces one via its built-in OpenAPI support.
- Generated types are vendored like shadcn components: never hand-edited, always regenerated.
- `src/lib/api.ts` is the only file that knows the base URL, auth header, and error shape.
  Components and query hooks call through it. Swapping backends should touch one file.
- **JSON is camelCase over the wire**, whichever language produces it. Configure the
  serializer on the server side; do not remap keys in the frontend.
- **Timestamps are ISO 8601 with an explicit offset, UTC on the wire.** No naive datetimes,
  no epoch integers, no `/Date(…)/`. Format for display at the edge with `date-fns`.
- **Money and precise decimals cross the wire as strings**, not floats. JS numbers cannot
  represent a C# `decimal` or a Python `Decimal` faithfully.
- Errors use one shape across every endpoint (RFC 9457 `application/problem+json` is a fine
  default — ASP.NET Core emits it natively and it is easy to match in Python). `api.ts`
  parses it into one `ApiError` type so error UI is written once.
- Enums cross as strings, not integers. An integer enum is unreadable in a network tab and
  breaks silently when the server reorders it.
- Pagination, filtering, and sorting are the server's job. TanStack Query caches whatever
  shape you settle on — pick one convention per project and write it below.
- Dev setup: the Vite dev server proxies `/api` to the backend so there is no CORS config
  and no environment-specific base URL in the client. Same-origin in production too, behind
  the reverse proxy.

## 8. Notes for agents

- Check whether a shadcn component already exists before building one. It usually does.
- Do not add a state library, a data-fetching library, or a UI kit. The stack is decided.
- Do not refactor unrelated files while completing a task.
- Prefer deleting code to adding an option. This is a hobby project; there is no user base
  to keep happy.
- Types come from the API contract, not from guesses. If the shape is unknown, ask rather
  than writing an interface that will silently drift.
- No `any`, no `@ts-expect-error` without a comment explaining the underlying issue.
- When unsure between two reasonable approaches, pick the one with less indirection and say
  in the PR why.

---

## 9. Project-specific

_Fill this in per repo. Everything above is shared and should stay identical across projects._

- **What this app is:** Audiobook Manager - full-stack web application that organizes m4b audiobook files into a structured library, with metadata scraping and Audiobookshelf integration.
- **Who uses it:** Self-hosters and personal audiobook library managers.
- **Router / framework choice and why:** React 19 + TanStack Router (SPA, file-based routes under `src/routes/`) with Vite.
- **Backend and where its OpenAPI spec lives:** ASP.NET Core Web API (net10.0), OpenAPI spec available via Swagger at `/swagger/v1/swagger.json`.
- **Pagination convention:** Offset-based (`limit`, `offset` query params) returning `{ count, total, items }` (`src/types/Common.ts`'s `PaginatedResult<T>`).
- **Deviations from the shared conventions (with reasons):**
  - `src/types/*.ts` is a mix of thin aliases over `api-types.ts` (`components["schemas"][...]`, narrowed to the fields the DTO's C# source actually guarantees non-null — see `src/lib/dto.ts`'s `Require` helper) and a handful of genuinely frontend-only shapes with no wire counterpart (`Audiobook`/`AudiobookPerson`, the tag-preview-only `OrganizeAudiobookInput`, `PaginatedResult<T>`, `UserNotificationError`). Each such file documents which case it is and why. This is a deliberate compromise on section 7's "never hand-write response interfaces": Swashbuckle only emits an OpenAPI `required` array for types carrying `[Required]` attributes, so directly aliasing `components["schemas"][...]` for this backend's plain-record response DTOs would make every field optional/nullable regardless of what the server actually sends.
  - Section 1's "pin the choice in `components.json`" for Base UI doesn't apply here: this project's `components.json` uses `"style": "default"` rather than one of the shadcn CLI's bundled presets (e.g. `base-nova`), and its `configSchema` is `.strict()` with no field for pinning a base on a non-preset style — adding one (e.g. `"base": "base"`) makes the CLI reject the whole config file (`Invalid configuration found`), confirmed via `npx shadcn@latest add button --dry-run`. There's currently no config-file mechanism to enforce this for this project's setup; it relies on convention (and this file) instead.
