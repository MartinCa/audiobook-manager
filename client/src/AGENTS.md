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

Git hooks (`lefthook`) format and lint files locally on `git commit`. However, AI agents frequently operate in ephemeral cloud VMs, Web/mobile sessions, or Docker containers where git hooks may not be initialized or executed automatically.

Before creating commits and opening a pull request, you **MUST** run all verification commands explicitly:

1. `pnpm run lint` — ESLint flat config with `--max-warnings 0` (enforcing strict TypeScript, TanStack Query best practices, import boundaries, and no Zustand fetches).
2. `pnpm run format-check` — Prettier verification (`prettier --check .`).
3. `tsc --noEmit` (or `pnpm exec tsc --noEmit`) — Full project type-checking.
4. `pnpm test` — Automated test suite.

Fix any reported violations or warnings rather than disabling rules or skipping checks.

## Shortcuts

- `shadcn info` — what is installed, which base, where the docs are.
- `shadcn docs <component>` — current API for a primitive. Use this instead of
  recalling props from memory; the Base UI and Radix APIs differ.
- `shadcn add <name> --dry-run` / `--view` — inspect before writing files.

## Dependency versions

Install packages with the package manager (`pnpm add <pkg>`, no version
pin) and let it resolve the current release. `pnpm add` still writes a
range into `package.json` — that's fine, and Renovate keeps that range
current from here on. What to avoid is typing the number yourself: do
not hand-write a version into `package.json` from memory — training data
lags, and a remembered version is routinely a major or two behind. If a
specific version genuinely matters (a peer dependency constraint, a
known-bad release), say so and name the reason in the commit, don't just
guess a number that looks plausible.

## House rules that are linted

`pnpm lint` enforces the mechanical parts of `DESIGN.md`: no `any`, no deep
relative imports, no direct primitive imports outside `components/ui/`, no
inline `style` props, no fetching inside a Zustand store (in either
`create(init)` or the curried `create()(init)` form), and TanStack Query
best practices via `@tanstack/eslint-plugin-query` (exhaustive query key dependencies,
stable query clients, mutation property order). A few rules are warnings
rather than errors, so CI runs with `--max-warnings 0` — a warning is not a
pass, it is a thing to fix. If a rule fires,
fix the code rather than disabling the rule. If the rule is genuinely wrong,
say so and change it upstream in `@martinrun/frontend-config`.

## Do not

- Add a state, data-fetching, or UI library. The stack is decided in `DESIGN.md`.
- Hand-edit `src/components/ui/**`, `src/lib/api-types.ts`, or `src/routeTree.gen.ts`. All are vendored.
- Refactor files unrelated to the task in hand.
- Write a response interface by hand. Regenerate from the OpenAPI spec.
