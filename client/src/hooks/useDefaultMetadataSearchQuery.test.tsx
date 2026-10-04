import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useDefaultMetadataSearchQuery } from "./useDefaultMetadataSearchQuery";
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
  };
}

const AUTHORS = ["George R. R. Martin"];
const BOOK = "A Knight of the Seven Kingdoms";

describe("useDefaultMetadataSearchQuery", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("uses the as-stored query and never asks the server when handling is AsStored", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue(settingsWith("AsStored"));

    const { result } = renderHook(
      () => useDefaultMetadataSearchQuery(AUTHORS, BOOK, "file.m4b", true),
      { wrapper },
    );

    await waitFor(() => expect(settingsApi.getLibrarySettings).toHaveBeenCalled());
    expect(result.current).toBe("George R. R. Martin - A Knight of the Seven Kingdoms");
    expect(metadataSearchApi.getDefaultQuery).not.toHaveBeenCalled();
  });

  it("adopts the server-built query when the library rewrites initials", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue(settingsWith("Compact"));
    vi.mocked(metadataSearchApi.getDefaultQuery).mockResolvedValue({
      query: "George R.R. Martin - A Knight of the Seven Kingdoms",
    });

    const { result } = renderHook(
      () => useDefaultMetadataSearchQuery(AUTHORS, BOOK, "file.m4b", true),
      { wrapper },
    );

    await waitFor(() =>
      expect(result.current).toBe("George R.R. Martin - A Knight of the Seven Kingdoms"),
    );
    expect(metadataSearchApi.getDefaultQuery).toHaveBeenCalledWith({
      authors: AUTHORS,
      bookName: BOOK,
      fileName: "file.m4b",
    });
  });

  it("does not ask the server while the dialog is closed", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue(settingsWith("Compact"));

    const { result } = renderHook(
      () => useDefaultMetadataSearchQuery(AUTHORS, BOOK, "file.m4b", false),
      { wrapper },
    );

    await waitFor(() => expect(settingsApi.getLibrarySettings).toHaveBeenCalled());
    expect(metadataSearchApi.getDefaultQuery).not.toHaveBeenCalled();
    expect(result.current).toBe("George R. R. Martin - A Knight of the Seven Kingdoms");
  });

  it("falls back to the as-stored query when the server request fails", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue(settingsWith("Spaced"));
    vi.mocked(metadataSearchApi.getDefaultQuery).mockRejectedValue(new Error("boom"));

    const { result } = renderHook(
      () => useDefaultMetadataSearchQuery(AUTHORS, BOOK, "file.m4b", true),
      { wrapper },
    );

    await waitFor(() => expect(metadataSearchApi.getDefaultQuery).toHaveBeenCalled());
    expect(result.current).toBe("George R. R. Martin - A Knight of the Seven Kingdoms");
  });
});
