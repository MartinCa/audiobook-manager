import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useStartLibraryScan } from "./useStartLibraryScan";
import { ApiError } from "@/lib/api";

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

vi.mock("@/services/api", () => ({
  libraryApi: {
    startScan: vi.fn(),
  },
}));

import { libraryApi } from "@/services/api";
import { notifications } from "@/lib/notifications";

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: false } },
});

function renderTrigger() {
  return renderHook(() => useStartLibraryScan(), {
    wrapper: ({ children }) => (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    ),
  });
}

describe("useStartLibraryScan", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    queryClient.clear();
  });

  it("starts the combined scan endpoint and toasts the started-in-background message", async () => {
    vi.mocked(libraryApi.startScan).mockResolvedValue(undefined);

    const { result } = renderTrigger();

    await result.current.startScan();

    expect(libraryApi.startScan).toHaveBeenCalledTimes(1);
    expect(notifications.success).toHaveBeenCalledWith("Library scan started in background");
    expect(notifications.error).not.toHaveBeenCalled();
    expect(result.current.isStarting).toBe(false);
  });

  it("surfaces the RFC 9457 problem detail when the start request is refused", async () => {
    vi.mocked(libraryApi.startScan).mockRejectedValue(
      new ApiError(409, {
        title: "Library unavailable",
        status: 409,
        detail:
          "The library directory '/media/audiobooks' is not available, so every book would look missing.",
      }),
    );

    const { result } = renderTrigger();

    await expect(result.current.startScan()).rejects.toBeInstanceOf(ApiError);

    expect(notifications.error).toHaveBeenCalledWith(
      "The library directory '/media/audiobooks' is not available, so every book would look missing.",
    );
    expect(notifications.success).not.toHaveBeenCalled();
  });

  it("falls back to the error message when the failure carries no problem detail", async () => {
    vi.mocked(libraryApi.startScan).mockRejectedValue(
      new ApiError(500, { title: "Internal Server Error", status: 500 }),
    );

    const { result } = renderTrigger();

    await expect(result.current.startScan()).rejects.toBeInstanceOf(ApiError);

    expect(notifications.error).toHaveBeenCalledWith("Internal Server Error");
  });

  it("does not claim the run is starting before the request settles", async () => {
    let release!: () => void;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    vi.mocked(libraryApi.startScan).mockReturnValue(gate);

    const { result } = renderTrigger();

    const pending = result.current.startScan();
    // The mutation's pending state is dispatched to subscribers through the query client, so
    // assert it via waitFor rather than immediately after the call.
    await waitFor(() => {
      expect(result.current.isStarting).toBe(true);
    });

    release();
    await pending;

    await waitFor(() => {
      expect(result.current.isStarting).toBe(false);
      expect(notifications.success).toHaveBeenCalledWith("Library scan started in background");
    });
  });
});
