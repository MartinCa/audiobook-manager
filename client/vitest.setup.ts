import "@testing-library/jest-dom";

import type { TestingLibraryMatchers } from "@testing-library/jest-dom/matchers";

// --- jest-dom matcher types under vitest 5 ----------------------------------
// Vitest 5 inlined its expect package and changed the matcher interfaces: Assertion<T>
// became Assertion<R, T> (return type first), and the global jest.Matchers namespace that
// @testing-library/jest-dom's plain entry augments is no longer part of vitest's Assertion.
// jest-dom 7.0.1's own /vitest types still augment the v4 one-parameter Assertion<T>, which
// TypeScript silently drops (mismatched type parameters, suppressed by skipLibCheck) — so
// toBeInTheDocument & co. stopped type-checking. Augment the v5 shape here instead. Do NOT
// "fix" the runtime import above to @testing-library/jest-dom/vitest: its stale
// augmentation would conflict with this block. Once jest-dom ships vitest 5 support, delete
// this block and switch the import to @testing-library/jest-dom/vitest.
declare module "vitest" {
  // `T` must stay in the signature (even though only `R` is used below) so this declaration
  // merges with vitest's own two-parameter `Assertion<R, T>` rather than shadowing it; the
  // no-empty-object-type/no-unused-vars disables below are consequences of that merge, not
  // things a member or a differently-shaped signature could satisfy.
  // `any` for the asymmetric-matcher parameter mirrors jest-dom's own vitest shim.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any, @typescript-eslint/no-unused-vars, @typescript-eslint/no-empty-object-type
  interface Assertion<R, T> extends TestingLibraryMatchers<any, R> {}
  // eslint-disable-next-line @typescript-eslint/no-explicit-any, @typescript-eslint/no-empty-object-type
  interface AsymmetricMatchersContaining extends TestingLibraryMatchers<any, any> {}
}

// jsdom doesn't implement matchMedia; ThemeProvider (dark-mode detection) needs it whenever a
// test renders the app's root layout or anything wrapped in ThemeProvider.
if (typeof window !== "undefined" && !window.matchMedia) {
  window.matchMedia = (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  });
}

// jsdom doesn't implement elementFromPoint; FormKit drag-and-drop's native-drag handlers call
// it for scroll-container detection during an active drag (handleNodeDragover -> handleSynthScroll)
// and as the fallback in validateDragHandle. The input effect is null: a test's chips are small
// and not in a scroll container, so the harness below is enough to let native drag events flow -
// the actual sort decision runs on getBoundingClientRect geometry instead. Without it an active
// drag crashes on `document.elementFromPoint is not a function`.
if (typeof document !== "undefined" && !document.elementFromPoint) {
  document.elementFromPoint = () => null;
}

(globalThis as unknown as Record<string, string>).__APP_VERSION__ = "0.9.0-test";
(globalThis as unknown as Record<string, string>).__COMMIT_HASH__ = "test-sha";
