# AGENTS.md

## Before writing any UI code

Read `DESIGN.md` in this repository in full. It is binding. If a rule there
conflicts with a habit, a tutorial, or a suggestion from a model, the file wins.

`DESIGN.md` is distributed from `MartinCa/frontend-kit` and is **not edited here**.
To change a convention, change it upstream and reinstall:

```sh
pnpm dlx shadcn@latest add MartinCa/frontend-kit/conventions --overwrite
```

The project-specific section at the bottom of `DESIGN.md` is the exception — that
part is owned by this repo.

## Mandatory verification before opening PRs

Git hooks (`lefthook`) format and lint on `git commit`, but agents often work
where hooks are not initialized (ephemeral cloud VMs, web/mobile sessions, Docker
containers). Before creating commits and opening a pull request, you **MUST**
run all verification commands explicitly:

1. `pnpm run lint` — ESLint flat config with `--max-warnings 0`.
2. `pnpm run format-check` — Prettier verification (`prettier --check .`).
3. `tsc --noEmit` (or `pnpm exec tsc --noEmit`) — full project type-checking.
4. `pnpm test` — automated test suite.

Fix any reported violations or warnings rather than disabling rules or skipping checks.

## Shortcuts

- `shadcn info` — what is installed, which base, where the docs are.
- `shadcn docs <component>` — current API for a primitive. Use this instead of
  recalling props from memory; the Base UI and Radix APIs differ.
- `shadcn add <name> --dry-run` / `--view` — inspect before writing files.

## Dependency versions

Install packages with the package manager (`pnpm add <pkg>`, no version pin) and
let it resolve the current release; `pnpm add` writes a range and Renovate keeps
it current. Do not hand-write a version into `package.json` from memory — training
data lags, and a remembered version is routinely a major or two behind. If a
specific version genuinely matters (a peer dependency constraint, a known-bad
release), say so and name the reason in the commit.

## House rules that are linted

`pnpm lint` enforces the mechanical parts of `DESIGN.md` — strict TypeScript, no
deep relative imports, no direct primitive imports outside `components/ui/`, no
inline `style` props, no Zustand fetches, TanStack Query best practices; the rules
themselves live in `DESIGN.md`. A few are warnings rather than errors, so CI runs
with `--max-warnings 0`: a warning is a thing to fix, not a pass. Fix the code
rather than disabling the rule; if a rule is genuinely wrong, change it upstream
in `@martinrun/frontend-config`.

## Display-date convention

Every user-facing full date or date-time is formatted with `date-fns` in ISO calendar form —
never `toLocaleString`/`toLocaleDateString`/`Intl` and never a locale-dependent format:

- Full dates display as `yyyy-MM-dd`.
- Full date-times display as `yyyy-MM-dd HH:mm:ss` (24-hour time).

Route displays through the shared helpers `formatDate` / `formatDateTime` in
`src/helpers/formatHelpers.ts` rather than formatting inline. They render in the user's local
timezone; only the output _format_ is fixed. A date-only ISO value (`yyyy-MM-dd`) passed to
`formatDate` is a calendar date, not a timestamp — it is read as local midnight, never parsed
as UTC midnight (which would shift the displayed day off UTC) and never timezone-converted.
A value carrying a time part or an explicit offset is parsed as an instant and shown in the
user's timezone. When writing a `date-fns` token pattern, note the case: `yyyy`/`dd` are
lowercase (calendar year and day-of-month); `YYYY`/`DD` are not accepted tokens — do not
capitalize them.

## Do not

- Add a state, data-fetching, or UI library. The stack is decided in `DESIGN.md`.
- Hand-edit `src/components/ui/**` or generated files (`src/lib/api-types.ts`,
  `src/routeTree.gen.ts` where present). All are vendored.
- Refactor files unrelated to the task in hand.
- Write a response interface by hand. Regenerate from the OpenAPI spec.
