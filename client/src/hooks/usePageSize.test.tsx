import { describe, it, expect, vi } from "vitest";
import { renderHook, act, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { usePageSize } from "./usePageSize";
import { settingsApi } from "@/services/api";
import { PAGE_SIZE } from "@/constants/paging";

vi.mock("@/services/api", () => ({
  settingsApi: {
    getLibrarySettings: vi.fn(),
  },
}));

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

describe("usePageSize", () => {
  it("falls back to the shared PAGE_SIZE while the setting hasn't loaded", () => {
    vi.mocked(settingsApi.getLibrarySettings).mockReturnValue(new Promise(() => {}));

    const { result } = renderHook(() => usePageSize(), { wrapper });

    expect(result.current[0]).toBe(PAGE_SIZE);
  });

  it("adopts the library's default page size once it loads", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue({
      initialsSpacing: "Unspaced",
      initialsPunctuation: "Dotted",
      metadataRefreshDelayMs: 1000,
      upcomingReleasesEnabled: true,
      upcomingReleasesCronSchedule: "0 3 * * *",
      defaultPageSize: 100,
      searchInitialsHandling: "AsStored",
      includeNarratorInPath: false,
    });

    const { result } = renderHook(() => usePageSize(), { wrapper });

    await waitFor(() => expect(result.current[0]).toBe(100));
  });

  it("keeps a session override even after the setting resolves", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue({
      initialsSpacing: "Unspaced",
      initialsPunctuation: "Dotted",
      metadataRefreshDelayMs: 1000,
      upcomingReleasesEnabled: true,
      upcomingReleasesCronSchedule: "0 3 * * *",
      defaultPageSize: 20,
      searchInitialsHandling: "AsStored",
      includeNarratorInPath: false,
    });

    const { result } = renderHook(() => usePageSize(), { wrapper });

    await waitFor(() => expect(result.current[0]).toBe(20));

    act(() => {
      result.current[1](100);
    });

    expect(result.current[0]).toBe(100);
  });
});
