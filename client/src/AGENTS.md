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
`create(init)` or the curried `create()(init)` form). A few rules are warnings
rather than errors, so CI runs with `--max-warnings 0` — a warning is not a
pass, it is a thing to fix. If a rule fires,
fix the code rather than disabling the rule. If the rule is genuinely wrong,
say so and change it upstream in `@martinrun/frontend-config`.

## Do not

- Add a state, data-fetching, or UI library. The stack is decided in `DESIGN.md`.
- Hand-edit `src/components/ui/**`, `src/lib/api-types.ts`, or `src/routeTree.gen.ts`. All are vendored.
- Refactor files unrelated to the task in hand.
- Write a response interface by hand. Regenerate from the OpenAPI spec.
