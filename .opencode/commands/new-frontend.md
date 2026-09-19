---
description: Scaffold a new frontend project using the house stack and conventions.
---

Scaffold a new frontend in the current directory following the house conventions.

Run these in order, stopping to report if any step fails:

1. `pnpm dlx shadcn@latest init --template vite --base base --preset <code>`,
   where `<code>` is the preset code passed as the command argument
   (`$ARGUMENTS`). If the argument is empty, ask for the preset code — it is
   recorded in the frontend-kit README (this kit's is `b0`). Do not put an
   empty substitution inline after `--preset`; only run the command once you
   have the code.
2. `pnpm dlx shadcn@latest add MartinCa/frontend-kit/conventions`
3. `pnpm dlx shadcn@latest add MartinCa/frontend-kit/api-client MartinCa/frontend-kit/query-setup MartinCa/frontend-kit/theme MartinCa/frontend-kit/theme-provider`
4. `pnpm add -D @martinrun/frontend-config eslint prettier prettier-plugin-tailwindcss typescript lefthook`
5. `pnpm add @tanstack/react-query @tanstack/react-router zustand react-hook-form zod date-fns lucide-react`
6. `pnpm dlx skills add shadcn/ui`

Then wire up the config files as described in the frontend-kit SETUP.md:
`eslint.config.js`, `prettier.config.js`, `tsconfig.json` extending the shared
base, `pnpm-workspace.yaml` for pnpm's supply-chain settings, `package.json` scripts
(`lint`, `format-check`, `format`, `"prepare": "lefthook install"`), a `lefthook.yml`
running lint/format on staged files, `.github/workflows/ci.yml`
running validation steps (`pnpm run lint`, `pnpm run format-check`, `tsc --noEmit`,
`pnpm test`), and the `/api` dev proxy in `vite.config.ts`. Ensure `.gitignore`
does not ignore `src/routeTree.gen.ts` — it is a committed vendored contract file
required for type-aware linting on fresh clones.

Wrap the app in `<ThemeProvider>` from `src/components/theme-provider.tsx` in
`main.tsx` (see SETUP.md Part 6). This step is easy to skip because the app
renders fine either way — it just always renders light, since `theme.css`'s
`.dark` class is never toggled without it, regardless of the system
preference. Installing `theme-provider` alone does not do this.

Finally, fill in the project-specific section at the bottom of `DESIGN.md` by
asking what the app is, who uses it, and which backend it talks to. Do not guess.
