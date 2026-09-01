import { createContext, useContext, useEffect, useState, type ReactNode } from "react";

/**
 * Defaults to the OS preference and keeps the .dark class (see
 * src/styles/theme.css) in sync with it — including live changes, e.g. the
 * user's system switching at sunset. "light"/"dark" are explicit overrides,
 * persisted so a manual choice survives a reload.
 */
type Theme = "light" | "dark" | "system";

interface ThemeProviderState {
  theme: Theme;
  /**
   * What's actually applied right now — "system" resolved to light or dark.
   * Read this to render UI; never read the DOM directly.
   */
  resolvedTheme: "light" | "dark";
  setTheme: (theme: Theme) => void;
}

const ThemeProviderContext = createContext<ThemeProviderState | undefined>(undefined);

const DARK_MEDIA_QUERY = "(prefers-color-scheme: dark)";

/**
 * Guarded because this runs during render (in the useState initializer below),
 * not just in an effect. DESIGN.md section 1 allows Next.js where SSR is a
 * stated requirement, and there this executes on the server with no `window`.
 * Light is the right server default: it matches what the markup renders before
 * the effect below applies the real preference.
 */
function systemPrefersDark(): boolean {
  if (typeof window === "undefined") return false;
  return window.matchMedia(DARK_MEDIA_QUERY).matches;
}

/**
 * localStorage throws in some environments (Safari private browsing, a
 * sandboxed iframe, storage blocked by browser settings) — falling back to
 * an in-memory-only preference beats crashing the whole app over a theme.
 */
function readStoredTheme(storageKey: string): Theme | null {
  try {
    const stored = localStorage.getItem(storageKey);
    return stored === "light" || stored === "dark" || stored === "system" ? stored : null;
  } catch {
    return null;
  }
}

function writeStoredTheme(storageKey: string, theme: Theme): void {
  try {
    localStorage.setItem(storageKey, theme);
  } catch {
    // Preference just won't survive a reload — not worth surfacing.
  }
}

export function ThemeProvider({
  children,
  defaultTheme = "system",
  storageKey = "theme",
}: {
  children: ReactNode;
  defaultTheme?: Theme;
  storageKey?: string;
}) {
  const [theme, setThemeState] = useState<Theme>(() => readStoredTheme(storageKey) ?? defaultTheme);
  const [resolvedTheme, setResolvedTheme] = useState<"light" | "dark">(() =>
    theme === "system" ? (systemPrefersDark() ? "dark" : "light") : theme,
  );

  useEffect(() => {
    const root = document.documentElement;
    const apply = (dark: boolean) => {
      root.classList.toggle("dark", dark);
      setResolvedTheme(dark ? "dark" : "light");
    };

    if (theme !== "system") {
      apply(theme === "dark");
      return;
    }

    apply(systemPrefersDark());
    const media = window.matchMedia(DARK_MEDIA_QUERY);
    const onChange = () => apply(systemPrefersDark());
    media.addEventListener("change", onChange);
    return () => media.removeEventListener("change", onChange);
  }, [theme]);

  function setTheme(next: Theme) {
    writeStoredTheme(storageKey, next);
    setThemeState(next);
  }

  return (
    <ThemeProviderContext.Provider value={{ theme, resolvedTheme, setTheme }}>
      {children}
    </ThemeProviderContext.Provider>
  );
}

export function useTheme(): ThemeProviderState {
  const context = useContext(ThemeProviderContext);
  if (!context) throw new Error("useTheme must be used within a ThemeProvider");
  return context;
}
