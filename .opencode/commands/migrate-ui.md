---
description: Migrate an existing application's UI completely onto the frontend-kit stack (shadcn/ui, Base UI, Tailwind tokens).
---

Migrate this application's UI completely onto the frontend-kit stack, replacing legacy UI libraries and ad-hoc styling.

Follow these steps in order:

1. **Assess & Inventory**:
   - Identify all components imported from legacy UI libraries (MUI, Ant Design, Chakra, Bootstrap, etc.) and ad-hoc UI widgets.
   - List every screen/view and the legacy components it uses.
   - Confirm which Base UI-based shadcn/ui components will replace them.

2. **Ensure Foundation is Present**:
   - If `components.json` does not exist, run `pnpm dlx shadcn@latest init` (ask for the preset code if not already recorded).
   - Ensure `MartinCa/frontend-kit/conventions`, `api-client`, `query-setup`, `theme`, and `theme-provider` are installed.
   - Ensure `@martinrun/frontend-config` is wired up in `eslint.config.js`, `prettier.config.js`, and `tsconfig.json`.
   - Ensure `<ThemeProvider>` from `src/components/theme-provider.tsx` wraps the application root (e.g. `main.tsx`).

3. **Install Required shadcn/ui Primitives**:
   - For all needed primitives (button, dialog, dropdown-menu, input, table, etc.), install via:
     `pnpm dlx shadcn@latest add <component>`
   - Patch known Base UI quirks (RadioGroupItem centering, DialogContent/DialogFooter mobile constraints per DESIGN.md section 3).
   - Commit any newly vendored `src/components/ui/` files in an untouched commit before editing application code.

4. **Screen-by-Screen Migration**:
   - Migrate one screen or component family at a time:
     - Replace legacy UI primitives with shadcn/ui components.
     - Replace legacy icons with `lucide-react`.
     - Replace ad-hoc colors and style objects with Tailwind semantic tokens (`bg-background`, `text-foreground`, `border-border`, `status-*`).
     - Migrate forms to `react-hook-form` + `zod` and tables to TanStack Table where appropriate.
     - Preserve user flows, behavior, and ensure visible focus rings and mobile responsiveness (<640px viewport bounds, `w-full sm:w-auto` dialog buttons, `min-w-0 flex-1` truncation, and `break-all` on diffs/paths).
   - Commit each migrated screen or component group separately with a descriptive commit message.

5. **Decommission Legacy UI**:
   - Once all usages are eliminated, uninstall legacy packages with `pnpm remove <packages>`.
   - Remove dead stylesheets, custom emotion/styled wrappers, or legacy provider wrappers.
   - Verify with `pnpm lint` and `pnpm build`.
   - Clean any resolved items from the "Deviations" list in `DESIGN.md`.
