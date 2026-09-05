# DESIGN.md

Frontend conventions for this repository. These rules are binding for both humans and
coding agents. If a rule here conflicts with a suggestion from a model, a tutorial, or a
Stack Overflow answer, this file wins. If a rule here is genuinely wrong for this project,
change the file in the same PR as the code — do not silently deviate.

Agents: read this file in full before writing any UI code. Do not introduce a library that
is not listed in **Allowed dependencies** without asking first.

---

## 1. Stack

| Layer           | Choice                                                                                                                                 | Not this                                                                                                                         |
| --------------- | -------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------- |
| Language        | TypeScript, `strict: true`                                                                                                             | JavaScript, `any`                                                                                                                |
| UI runtime      | React (function components + hooks only)                                                                                               | class components                                                                                                                 |
| Build / routing | Vite + TanStack Router (SPA) _or_ Next.js App Router (when SSR/SEO is genuinely needed)                                                | CRA, Webpack by hand                                                                                                             |
| Components      | shadcn/ui                                                                                                                              | MUI, Ant, Chakra, Bootstrap                                                                                                      |
| Primitives      | Base UI (shadcn default since July 2026). Pin the choice in `components.json`.                                                         | mixing Radix and Base UI in one repo                                                                                             |
| Styling         | Tailwind CSS + CSS variables for theme tokens                                                                                          | CSS-in-JS, SCSS, inline `style={{}}`                                                                                             |
| Icons           | `lucide-react`                                                                                                                         | mixed icon sets                                                                                                                  |
| Server state    | TanStack Query                                                                                                                         | fetch-into-`useState`, fetch-into-Zustand                                                                                        |
| Client state    | `useState` → lifted props → Zustand (in that order)                                                                                    | Redux, Context as a state manager                                                                                                |
| Forms           | `react-hook-form` + `zod` (client-side validation only — the server validates independently)                                           | hand-rolled validation                                                                                                           |
| Tables          | TanStack Table (headless) + shadcn table primitives                                                                                    | ag-grid, custom sort logic                                                                                                       |
| Charts          | shadcn/ui Charts (Recharts)                                                                                                            | new charting lib per project                                                                                                     |
| Dates           | `date-fns`                                                                                                                             | moment, hand-rolled parsing                                                                                                      |
| Tests           | Vitest + Testing Library; Playwright only where a flow is worth it                                                                     | Enzyme, snapshot-everything                                                                                                      |
| Lint/format     | ESLint (typescript-eslint, TanStack Query plugin) + Prettier, pre-commit hooks (Lefthook), both enforced in CI with `--max-warnings 0` | per-file disables without a reason comment; a lint job that passes on warnings; Husky (unmaintained — no release since Nov 2024) |

**Default to the SPA path.** Most projects here are internal tools behind auth on a
private network. They do not need SSR, RSC, or an SEO story, and the client/server
component boundary is a recurring source of agent mistakes. Reach for Next.js only when
there is a stated reason, and write that reason in the project README. When using TanStack
Router, `src/routeTree.gen.ts` is committed to Git as a vendored contract (see Section 7).

### Allowed dependencies

Anything in the table above, plus: `cn` (shadcn's Tailwind class-merging engine — a
drop-in replacement for `clsx` + `tailwind-merge`, installed via `npx shadcn migrate cn`
on Tailwind v4 projects; new `shadcn init` scaffolds already use it — the migration
command is a no-op if the project doesn't already have `clsx`/`tailwind-merge` in
`lib/utils.ts`, so don't go looking for something to run on a fresh project),
`class-variance-authority` (these come with shadcn), `sonner` for toasts, `cmdk` for
command palettes.

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

### Known Base UI component quirks & overlay mobile safety

Found empirically, not documented by shadcn or Base UI — no build error, no lint
warning, no console message, just a UI bug the first time real content or a real
form hits the component. Patch these right after `add`, the same way you'd handle
the Accordion keyframe gotcha in [MIGRATION.md](../docs/MIGRATION.md).

**`radio-group.tsx`: the indicator doesn't self-center.** Base UI's
`Radio.Indicator` centers its own children (the dot icon) but not itself within
the root circle — unlike `checkbox.tsx`'s root, which already carries
`grid place-content-center` for the same reason. Add the same two classes to
`RadioGroupItem`'s root:

```diff
  <RadioPrimitive.Root
    className={cn(
-     "border-primary text-primary ... aspect-square h-4 w-4 cursor-pointer rounded-full border ...",
+     "border-primary text-primary ... grid aspect-square h-4 w-4 cursor-pointer place-content-center rounded-full border ...",
```

**`dialog.tsx` & `alert-dialog.tsx`: default classes clip and overflow on mobile.**
The upstream default classes (`w-full max-w-lg p-6 sm:rounded-lg`) cause modals on mobile
(<640px) to touch the viewport edges without margins, and tall modals (or when the
virtual keyboard opens) overflow past the screen height without scrolling, making footer
action buttons unclickable. Furthermore, defaulting to `sm:space-x-2` without `gap-2` in
`DialogFooter` / `AlertDialogFooter` results in zero vertical spacing between buttons when
stacked on mobile.

Update `DialogContent` and `DialogFooter` in `src/components/ui/dialog.tsx` (and mirror
in `src/components/ui/alert-dialog.tsx`):

```diff
  <DialogPrimitive.Content
    className={cn(
-     "bg-background data-open:animate-in data-closed:animate-out data-closed:fade-out-0 data-open:fade-in-0 data-closed:zoom-out-95 data-open:zoom-in-95 data-closed:slide-out-to-left-1/2 data-closed:slide-out-to-top-[48%] data-open:slide-in-from-left-1/2 data-open:slide-in-from-top-[48%] fixed top-[50%] left-[50%] z-50 grid w-full max-w-lg translate-x-[-50%] translate-y-[-50%] gap-4 border p-6 shadow-lg duration-200 sm:rounded-lg",
+     "bg-background data-open:animate-in data-closed:animate-out data-closed:fade-out-0 data-open:fade-in-0 data-closed:zoom-out-95 data-open:zoom-in-95 data-closed:slide-out-to-left-1/2 data-closed:slide-out-to-top-[48%] data-open:slide-in-from-left-1/2 data-open:slide-in-from-top-[48%] fixed top-[50%] left-[50%] z-50 grid w-[calc(100vw-2rem)] sm:w-full max-w-lg max-h-[90dvh] overflow-y-auto translate-x-[-50%] translate-y-[-50%] gap-4 border p-4 sm:p-6 shadow-lg duration-200 rounded-lg sm:rounded-lg",
      className
    )}
```

```diff
  function DialogFooter({ className, ...props }: React.ComponentProps<"div">) {
    return (
      <div
        className={cn(
-         "flex flex-col-reverse sm:flex-row sm:justify-end sm:space-x-2",
+         "flex flex-col-reverse sm:flex-row sm:justify-end gap-2",
          className
        )}
        {...props}
      />
    )
  }
```

**Dialog Footer Buttons recipe:**
Buttons inside `DialogFooter` and `AlertDialogFooter` should use `className="w-full sm:w-auto"`
so they provide full-width touch targets on mobile and natural widths on desktop:

```tsx
<DialogFooter>
  <Button variant="outline" className="w-full sm:w-auto" onClick={() => setOpen(false)}>
    Cancel
  </Button>
  <Button className="w-full sm:w-auto" type="submit">
    Save changes
  </Button>
</DialogFooter>
```

**Complex dialogs with inner scrolling:**
For dialogs containing a scrollable table or list alongside fixed header and footer rows,
structure the content as a flex column with `overflow-hidden` so inner scroll regions do
not nest dual scrollbars:

```tsx
<DialogContent className="flex max-h-[90dvh] flex-col overflow-hidden p-4 sm:p-6">
  <DialogHeader>...</DialogHeader>
  <div className="min-h-0 flex-1 overflow-y-auto">...</div>
  <DialogFooter>...</DialogFooter>
</DialogContent>
```

**Other overlay components (`sheet.tsx`, `popover.tsx`, `dropdown-menu.tsx`):**

- **`sheet.tsx` (`SheetContent`)**: Ensure tall content has `overflow-y-auto` and viewport-safe
  bounds (`max-h-[100dvh]`) so actions remain reachable on mobile.
- **`popover.tsx` (`PopoverContent`) & `dropdown-menu.tsx` (`DropdownMenuContent`)**:
  Ensure contents enforce `max-h-[90dvh] overflow-y-auto` and viewport collision padding so
  large menus or complex popovers do not clip beyond mobile viewports.

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
    api-types.ts    generated OpenAPI types — vendored, do not hand-edit
    utils.ts        cn() and friends
  routeTree.gen.ts  generated TanStack Router tree — vendored, do not hand-edit
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
- **Responsive down to a phone (375px).** If a table cannot work at 375px, show a card list instead.
  - **Flex child text truncation**: In flex rows where text sits alongside fixed-width elements (badges, buttons, icons), the text container must have `min-w-0 flex-1` for `truncate` or `break-words` to take effect and prevent horizontal scrollbars.
  - **Monospace & diff blocks**: Components displaying arbitrary paths, URLs, commit hashes, or code/diff blocks must include `break-all` alongside `whitespace-pre-wrap` (or an explicit `overflow-x-auto` container) so unbroken strings wrap cleanly on narrow screens.

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
- Responsive down to 375px wide with zero accidental horizontal scrolling. Dialogs and overlays
  are bounded to viewport heights (`max-h-[90dvh]` / `max-h-[100dvh]`) with vertical scrolling,
  and action buttons in footers stack cleanly with full touch targets (`w-full sm:w-auto`).

---

## 7. Backend contract

This stack is backend-agnostic. It talks HTTP and JSON and does not care whether the server
is ASP.NET Core, FastAPI, Flask, or a Go binary. The rules below keep it that way.

- **OpenAPI is the contract.** The backend publishes a spec; the frontend generates types
  from it into `src/lib/api-types.ts` and never hand-writes response interfaces. Regenerate
  as a checked-in build step so the diff is visible in review. FastAPI produces a spec from
  its models automatically; ASP.NET Core produces one via its built-in OpenAPI support.
- **Generated contract files are committed and vendored.** Both backend types
  (`src/lib/api-types.ts` from OpenAPI) and routing definitions (`src/routeTree.gen.ts`
  from TanStack Router) must be committed to Git. Treat them as vendored: never hand-edited,
  always regenerated. Committing `src/routeTree.gen.ts` ensures fresh clones have complete
  route types for IDEs and type-aware linting (`projectService: true`) without requiring an
  upfront build. TanStack Router treats `routeTree.gen.ts` as part of application source code.
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
- Do not hand-edit `src/components/ui/**`, `src/lib/api-types.ts`, or `src/routeTree.gen.ts`. All are vendored.
- Guarantee mobile (<640px) responsiveness on all UI additions: use `w-[calc(100vw-2rem)] sm:w-full`
  and `max-h-[90dvh] overflow-y-auto` on dialogs, `gap-2` and `w-full sm:w-auto` for footer buttons,
  `min-w-0 flex-1` on flex text containers, and `break-all` on paths/URLs/hashes.
- Do not refactor unrelated files while completing a task.
- Prefer deleting code to adding an option. This is a hobby project; there is no user base
  to keep happy.
- Types come from the API contract, not from guesses. If the shape is unknown, ask rather
  than writing an interface that will silently drift.
- No `any`, no `@ts-expect-error` without a comment explaining the underlying issue.
- Always run the mandatory verification commands before opening a PR: `pnpm run lint` (`--max-warnings 0`), `pnpm run format-check`, `tsc --noEmit`, and `pnpm test`.
- When unsure between two reasonable approaches, pick the one with less indirection and say
  in the PR why.

---

## 9. Project-specific

_Fill this in per repo. Everything above is shared and should stay identical across projects._

- **What this app is:**
- **Who uses it:**
- **Router / framework choice and why:**
- **Backend and where its OpenAPI spec lives:**
- **Pagination convention:**
- **Deviations from the shared conventions (with reasons):**
