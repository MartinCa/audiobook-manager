import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { useOperationResync } from "./useOperationResync";

let capturedReconnectedHandler: (() => void) | null = null;

vi.mock("@/hooks/useSignalR", () => ({
  useSignalRReconnected: (callback: () => void) => {
    capturedReconnectedHandler = callback;
  },
}));

vi.mock("@/services/api", () => ({
  operationsApi: {
    getStatus: vi.fn(),
  },
}));

import { operationsApi } from "@/services/api";

describe("useOperationResync", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    capturedReconnectedHandler = null;
  });

  it("fetches operation status on mount and calls onStatus", async () => {
    const onStatus = vi.fn();
    vi.mocked(operationsApi.getStatus).mockResolvedValue({
      isRunning: true,
      processed: 5,
      total: 10,
    });

    renderHook(() => useOperationResync("test-op", onStatus));

    await waitFor(() => {
      expect(operationsApi.getStatus).toHaveBeenCalledWith("test-op");
      expect(onStatus).toHaveBeenCalledWith({
        isRunning: true,
        processed: 5,
        total: 10,
      });
    });
  });

  it("refreshes status on SignalR reconnect", async () => {
    const onStatus = vi.fn();
    vi.mocked(operationsApi.getStatus).mockResolvedValue({
      isRunning: false,
      processed: 10,
      total: 10,
    });

    renderHook(() => useOperationResync("test-op", onStatus));

    await waitFor(() => {
      expect(operationsApi.getStatus).toHaveBeenCalledTimes(1);
    });

    expect(capturedReconnectedHandler).toBeTypeOf("function");
    capturedReconnectedHandler?.();

    await waitFor(() => {
      expect(operationsApi.getStatus).toHaveBeenCalledTimes(2);
    });
  });

  it("does not call onStatus if unmounted before response resolves", async () => {
    const onStatus = vi.fn();
    let resolvePromise: (val: unknown) => void;
    const delayedPromise = new Promise((resolve) => {
      resolvePromise = resolve;
    });

    vi.mocked(operationsApi.getStatus).mockReturnValue(delayedPromise as never);

    const { unmount } = renderHook(() => useOperationResync("test-op", onStatus));

    unmount();
    resolvePromise!({
      isRunning: true,
      processed: 1,
      total: 1,
    });

    await new Promise((r) => setTimeout(r, 10));
    expect(onStatus).not.toHaveBeenCalled();
  });
});
