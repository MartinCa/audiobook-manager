import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useResolveDefaultMetadataSearchQuery } from "./useResolveDefaultMetadataSearchQuery";
import { metadataSearchApi, settingsApi } from "@/services/api";
import type { LibrarySettings, SearchInitialsHandling } from "@/types/LibrarySettings";

vi.mock("@/services/api", () => ({
  settingsApi: { getLibrarySettings: vi.fn() },
  metadataSearchApi: { getDefaultQuery: vi.fn() },
}));

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

function settingsWith(handling: SearchInitialsHandling): LibrarySettings {
  return {
    initialsSpacing: "Unspaced",
    initialsPunctuation: "Dotted",
    metadataRefreshDelayMs: 1000,
    upcomingReleasesEnabled: true,
    upcomingReleasesCronSchedule: "0 3 * * *",
    defaultPageSize: 20,
    searchInitialsHandling: handling,
    includeNarratorInPath: false,
    maxNarratorsInPath: 3,
    autoSplitTitleOnColon: false,
    mainAuthorOnlyInPath: false,
    searchInitialsHandlingBySource: {},
  };
}

const AUTHORS = ["George R. R. Martin"];
const BOOK = "A Knight of the Seven Kingdoms";
const AS_STORED = "George R. R. Martin - A Knight of the Seven Kingdoms";

function resolver() {
  return renderHook(() => useResolveDefaultMetadataSearchQuery(), { wrapper }).result.current;
}

describe("useResolveDefaultMetadataSearchQuery", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("resolves the as-stored query and never asks the server when handling is AsStored", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue(settingsWith("AsStored"));

    const resolved = await resolver()(AUTHORS, BOOK, "file.m4b");

    expect(resolved).toEqual({ query: AS_STORED });
    expect(metadataSearchApi.getDefaultQuery).not.toHaveBeenCalled();
  });

  it("resolves the server-built query when the library rewrites initials", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue(settingsWith("Compact"));
    vi.mocked(metadataSearchApi.getDefaultQuery).mockResolvedValue({
      query: "George R.R. Martin - A Knight of the Seven Kingdoms",
    });

    const resolved = await resolver()(AUTHORS, BOOK, "file.m4b");

    expect(resolved).toEqual({ query: "George R.R. Martin - A Knight of the Seven Kingdoms" });
    expect(metadataSearchApi.getDefaultQuery).toHaveBeenCalledWith({
      authors: AUTHORS,
      bookName: BOOK,
      fileName: "file.m4b",
      mainAuthor: undefined,
    });
  });

  it("falls back to the as-stored query when the server request fails", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue(settingsWith("Spaced"));
    vi.mocked(metadataSearchApi.getDefaultQuery).mockRejectedValue(new Error("boom"));

    expect(await resolver()(AUTHORS, BOOK, "file.m4b")).toEqual({ query: AS_STORED });
  });

  it("falls back to the as-stored query when the settings cannot be read", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockRejectedValue(new Error("boom"));

    expect(await resolver()(AUTHORS, BOOK, "file.m4b")).toEqual({ query: AS_STORED });
    expect(metadataSearchApi.getDefaultQuery).not.toHaveBeenCalled();
  });

  it("does not retry a failing settings request before falling back", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockRejectedValue(new Error("boom"));
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: 3, retryDelay: 1 } },
    });
    const { result } = renderHook(() => useResolveDefaultMetadataSearchQuery(), {
      wrapper: ({ children }: { children: ReactNode }) => (
        <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
      ),
    });

    expect(await result.current(AUTHORS, BOOK, "file.m4b")).toEqual({ query: AS_STORED });
    // One attempt from the mount prefetch or the resolver, never the app's retry budget.
    expect(vi.mocked(settingsApi.getLibrarySettings).mock.calls.length).toBeLessThanOrEqual(2);
  });

  it("prefetches the library settings on mount so opening the dialog is normally instant", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue(settingsWith("AsStored"));

    renderHook(() => useResolveDefaultMetadataSearchQuery(), { wrapper });

    await waitFor(() => expect(settingsApi.getLibrarySettings).toHaveBeenCalledTimes(1));
  });

  describe("main author and per-source handling", () => {
    it("uses the main author alone in the as-stored query, without asking the server", async () => {
      vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue(settingsWith("AsStored"));

      const resolved = await resolver()(["A One", "B Two"], "Book", "file.m4b", "A One");

      expect(resolved).toEqual({ query: "A One - Book" });
      expect(metadataSearchApi.getDefaultQuery).not.toHaveBeenCalled();
    });

    it("sends the main author to the server when the library rewrites initials", async () => {
      vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue(settingsWith("Compact"));
      vi.mocked(metadataSearchApi.getDefaultQuery).mockResolvedValue({ query: "A One - Book" });

      await resolver()(["A One", "B Two"], "Book", "file.m4b", "A One");

      expect(metadataSearchApi.getDefaultQuery).toHaveBeenCalledWith({
        authors: ["A One", "B Two"],
        bookName: "Book",
        fileName: "file.m4b",
        mainAuthor: "A One",
      });
    });

    it("still asks the server when only a single source overrides an otherwise as-stored library", async () => {
      vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue({
        ...settingsWith("AsStored"),
        searchInitialsHandlingBySource: { Hardcover: "Compact" },
      });
      vi.mocked(metadataSearchApi.getDefaultQuery).mockResolvedValue({
        query: AS_STORED,
        queriesBySource: {
          Audible: AS_STORED,
          Hardcover: "George R.R. Martin - A Knight of the Seven Kingdoms",
        },
      });

      const resolved = await resolver()(AUTHORS, BOOK, "file.m4b");

      expect(metadataSearchApi.getDefaultQuery).toHaveBeenCalledTimes(1);
      expect(resolved).toEqual({
        query: AS_STORED,
        queriesBySource: {
          Audible: AS_STORED,
          Hardcover: "George R.R. Martin - A Knight of the Seven Kingdoms",
        },
      });
    });

    it("does not ask the server when every override is itself AsStored", async () => {
      vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue({
        ...settingsWith("AsStored"),
        searchInitialsHandlingBySource: { Hardcover: "AsStored" },
      });

      const resolved = await resolver()(AUTHORS, BOOK, "file.m4b");

      expect(resolved).toEqual({ query: AS_STORED });
      expect(metadataSearchApi.getDefaultQuery).not.toHaveBeenCalled();
    });

    it("seeds the one query every source agrees on, even when it differs from the library-wide form", async () => {
      vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue({
        ...settingsWith("AsStored"),
        searchInitialsHandlingBySource: { Audible: "Compact", Hardcover: "Compact" },
      });
      vi.mocked(metadataSearchApi.getDefaultQuery).mockResolvedValue({
        query: AS_STORED,
        queriesBySource: { Audible: "G.R.R. Martin - Book", Hardcover: "G.R.R. Martin - Book" },
      });

      const resolved = await resolver()(AUTHORS, BOOK, "file.m4b");

      // No map: the box shows exactly what every source will be asked.
      expect(resolved).toEqual({ query: "G.R.R. Martin - Book" });
    });

    it("keeps the library-wide text in the box and the per-source map when sources disagree", async () => {
      vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue({
        ...settingsWith("Spaced"),
        searchInitialsHandlingBySource: { Hardcover: "Compact" },
      });
      vi.mocked(metadataSearchApi.getDefaultQuery).mockResolvedValue({
        query: "George R. R. Martin - Book",
        queriesBySource: {
          Audible: "George R. R. Martin - Book",
          Hardcover: "George R.R. Martin - Book",
        },
      });

      const resolved = await resolver()(AUTHORS, "Book", "file.m4b");

      expect(resolved.query).toBe("George R. R. Martin - Book");
      expect(resolved.queriesBySource).toEqual({
        Audible: "George R. R. Martin - Book",
        Hardcover: "George R.R. Martin - Book",
      });
    });

    it("does not reuse a cached query across different per-source settings or main authors", async () => {
      vi.mocked(metadataSearchApi.getDefaultQuery).mockResolvedValue({ query: "q" });
      const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      const { result } = renderHook(() => useResolveDefaultMetadataSearchQuery(), {
        wrapper: ({ children }: { children: ReactNode }) => (
          <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
        ),
      });

      vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue({
        ...settingsWith("Compact"),
        searchInitialsHandlingBySource: {},
      });
      await result.current(AUTHORS, BOOK, "f.m4b", "A");
      await result.current(AUTHORS, BOOK, "f.m4b", "B");

      expect(metadataSearchApi.getDefaultQuery).toHaveBeenCalledTimes(2);
    });
  });
});
