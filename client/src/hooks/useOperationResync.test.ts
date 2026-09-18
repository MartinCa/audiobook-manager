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

  it("re-fetches status when resyncTrigger changes", async () => {
    const onStatus = vi.fn();
    vi.mocked(operationsApi.getStatus).mockResolvedValue({
      isRunning: true,
      processed: 5,
      total: 10,
    });

    const { rerender } = renderHook(
      ({ trigger }) => useOperationResync("test-op", onStatus, trigger),
      { initialProps: { trigger: 0 } },
    );

    await waitFor(() => {
      expect(operationsApi.getStatus).toHaveBeenCalledTimes(1);
    });

    rerender({ trigger: 1 });

    await waitFor(() => {
      expect(operationsApi.getStatus).toHaveBeenCalledTimes(2);
      expect(onStatus).toHaveBeenCalledTimes(2);
    });
  });

  it("drops an in-flight status response from the previous resyncTrigger", async () => {
    // The permanently-mounted dialog's open-triggered resync must not apply a status fetched
    // for the previous (closed) window: an old "running" response landing after the dialog was
    // reopened would resurrect a progress bar for a batch that already finished. The previous
    // effect instance's cleanup discards it.
    const onStatus = vi.fn();
    let resolveFirst: (val: unknown) => void;
    const first = new Promise((resolve) => {
      resolveFirst = resolve;
    });
    vi.mocked(operationsApi.getStatus)
      .mockReturnValueOnce(first as never)
      .mockResolvedValueOnce({ isRunning: true, processed: 2, total: 5 });

    const { rerender } = renderHook(
      ({ trigger }) => useOperationResync("test-op", onStatus, trigger),
      { initialProps: { trigger: 0 } },
    );

    await waitFor(() => {
      expect(operationsApi.getStatus).toHaveBeenCalledTimes(1);
    });

    rerender({ trigger: 1 });

    await waitFor(() => {
      expect(operationsApi.getStatus).toHaveBeenCalledTimes(2);
    });

    // The first (now stale) response resolves AFTER the trigger changed - it must be dropped.
    resolveFirst!({ isRunning: true, processed: 1, total: 5 });

    await new Promise((r) => setTimeout(r, 10));
    expect(onStatus).toHaveBeenCalledTimes(1);
    expect(onStatus).toHaveBeenCalledWith({ isRunning: true, processed: 2, total: 5 });
  });

  it("discards a stale isRunning:false response when an event invalidated the resync in flight", async () => {
    // Regression for the resync-vs-event race (stale-false direction): a mount fetch captures
    // the operation idling, a real progress event then sets the live progress state (the
    // consumer calls the returned invalidate from that handler), and the pre-event response
    // resolving afterwards must NOT clobber the event's state back to idle.
    const onStatus = vi.fn();
    let resolvePromise: (val: unknown) => void;
    const delayedPromise = new Promise((resolve) => {
      resolvePromise = resolve;
    });
    vi.mocked(operationsApi.getStatus).mockReturnValue(delayedPromise as never);

    let invalidate: () => void = () => {};
    renderHook(() => {
      invalidate = useOperationResync("test-op", onStatus);
    });

    await waitFor(() => {
      expect(operationsApi.getStatus).toHaveBeenCalledTimes(1);
    });

    // The consumer's SignalR progress handler invalidates the resync; the event it received is
    // newer truth than any in-flight status snapshot.
    invalidate();
    resolvePromise!({ isRunning: false, processed: 0, total: 0 });

    await new Promise((r) => setTimeout(r, 10));
    expect(onStatus).not.toHaveBeenCalled();
  });

  it("discards a late isRunning:true response when the completion event invalidated the resync", async () => {
    // Regression for the resync-vs-event race (late-true direction): a mount fetch captures the
    // operation running, the completion event then clears the running state (the consumer calls
    // the returned invalidate from that handler), and the pre-completion response resolving
    // afterwards must NOT resurrect the progress bar.
    const onStatus = vi.fn();
    let resolvePromise: (val: unknown) => void;
    const delayedPromise = new Promise((resolve) => {
      resolvePromise = resolve;
    });
    vi.mocked(operationsApi.getStatus).mockReturnValue(delayedPromise as never);

    let invalidate: () => void = () => {};
    renderHook(() => {
      invalidate = useOperationResync("test-op", onStatus);
    });

    await waitFor(() => {
      expect(operationsApi.getStatus).toHaveBeenCalledTimes(1);
    });

    invalidate();
    resolvePromise!({ isRunning: true, processed: 5, total: 5 });

    await new Promise((r) => setTimeout(r, 10));
    expect(onStatus).not.toHaveBeenCalled();
  });

  it("discards an older in-flight response when a newer status fetch was started", async () => {
    // Resync-vs-resync: a reconnect refresh supersedes the still-in-flight mount fetch. The
    // older response lands last and must not overwrite the newer request's fresher truth.
    const onStatus = vi.fn();
    let resolveFirst: (val: unknown) => void;
    const first = new Promise((resolve) => {
      resolveFirst = resolve;
    });
    vi.mocked(operationsApi.getStatus)
      .mockReturnValueOnce(first as never)
      .mockResolvedValueOnce({ isRunning: true, processed: 2, total: 5 });

    renderHook(() => useOperationResync("test-op", onStatus));

    await waitFor(() => {
      expect(operationsApi.getStatus).toHaveBeenCalledTimes(1);
    });

    capturedReconnectedHandler?.();

    await waitFor(() => {
      expect(operationsApi.getStatus).toHaveBeenCalledTimes(2);
    });

    // The older mount response arrives only now - after the newer fetch already applied.
    resolveFirst!({ isRunning: false, processed: 0, total: 0 });

    await waitFor(() => {
      expect(onStatus).toHaveBeenCalledTimes(1);
      expect(onStatus).toHaveBeenCalledWith({ isRunning: true, processed: 2, total: 5 });
    });
  });

  it("still applies a response fetched after an event invalidated the resync", async () => {
    // Invalidation discards only responses that were in flight when the event arrived - a fetch
    // started afterwards captures the new generation and must apply normally, so a stale event
    // cannot permanently suppress recovery.
    const onStatus = vi.fn();
    let resolveFirst: (val: unknown) => void;
    const first = new Promise((resolve) => {
      resolveFirst = resolve;
    });
    vi.mocked(operationsApi.getStatus)
      .mockReturnValueOnce(first as never)
      .mockResolvedValueOnce({ isRunning: true, processed: 2, total: 5 });

    let invalidate: () => void = () => {};
    renderHook(() => {
      invalidate = useOperationResync("test-op", onStatus);
    });

    await waitFor(() => {
      expect(operationsApi.getStatus).toHaveBeenCalledTimes(1);
    });

    invalidate();
    capturedReconnectedHandler?.();

    // The post-invalidation reconnect fetch resolves and applies...
    await waitFor(() => {
      expect(operationsApi.getStatus).toHaveBeenCalledTimes(2);
      expect(onStatus).toHaveBeenCalledWith({ isRunning: true, processed: 2, total: 5 });
    });

    // ...while the pre-invalidation mount response is still dropped.
    resolveFirst!({ isRunning: false, processed: 0, total: 0 });

    await new Promise((r) => setTimeout(r, 10));
    expect(onStatus).toHaveBeenCalledTimes(1);
  });
});
