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

    const query = await resolver()(AUTHORS, BOOK, "file.m4b");

    expect(query).toBe(AS_STORED);
    expect(metadataSearchApi.getDefaultQuery).not.toHaveBeenCalled();
  });

  it("resolves the server-built query when the library rewrites initials", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue(settingsWith("Compact"));
    vi.mocked(metadataSearchApi.getDefaultQuery).mockResolvedValue({
      query: "George R.R. Martin - A Knight of the Seven Kingdoms",
    });

    const query = await resolver()(AUTHORS, BOOK, "file.m4b");

    expect(query).toBe("George R.R. Martin - A Knight of the Seven Kingdoms");
    expect(metadataSearchApi.getDefaultQuery).toHaveBeenCalledWith({
      authors: AUTHORS,
      bookName: BOOK,
      fileName: "file.m4b",
    });
  });

  it("falls back to the as-stored query when the server request fails", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue(settingsWith("Spaced"));
    vi.mocked(metadataSearchApi.getDefaultQuery).mockRejectedValue(new Error("boom"));

    expect(await resolver()(AUTHORS, BOOK, "file.m4b")).toBe(AS_STORED);
  });

  it("falls back to the as-stored query when the settings cannot be read", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockRejectedValue(new Error("boom"));

    expect(await resolver()(AUTHORS, BOOK, "file.m4b")).toBe(AS_STORED);
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

    expect(await result.current(AUTHORS, BOOK, "file.m4b")).toBe(AS_STORED);
    // One attempt from the mount prefetch or the resolver, never the app's retry budget.
    expect(vi.mocked(settingsApi.getLibrarySettings).mock.calls.length).toBeLessThanOrEqual(2);
  });

  it("prefetches the library settings on mount so opening the dialog is normally instant", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue(settingsWith("AsStored"));

    renderHook(() => useResolveDefaultMetadataSearchQuery(), { wrapper });

    await waitFor(() => expect(settingsApi.getLibrarySettings).toHaveBeenCalledTimes(1));
  });
});
