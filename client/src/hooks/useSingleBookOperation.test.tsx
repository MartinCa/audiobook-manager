import type { ReactNode } from "react";
import { describe, it, expect, vi } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { SignalRContext } from "@/context/SignalRContext";
import { SignalREvents, SingleBookOperations } from "@/constants/signalrEvents";
import { createFakeSignalR } from "@/test-utils/fakeSignalR";
import { useSingleBookOperation } from "./useSingleBookOperation";

function setup({ connected = true }: { connected?: boolean } = {}) {
  const hub = createFakeSignalR();
  let isConnected = connected;
  const onComplete = vi.fn();
  const onResync = vi.fn();
  // Rebuilt on every render so a test can flip the connection state and rerender.
  const wrapper = ({ children }: { children: ReactNode }) => (
    <SignalRContext.Provider value={{ ...hub.value, isConnected }}>
      {children}
    </SignalRContext.Provider>
  );
  const { result, rerender } = renderHook(
    () => useSingleBookOperation(SingleBookOperations.pendingMatchSelect, onComplete, onResync),
    { wrapper },
  );
  return {
    hub,
    result,
    onComplete,
    onResync,
    setConnected: (next: boolean) => {
      isConnected = next;
      rerender();
    },
  };
}

const completion = (overrides: Record<string, unknown> = {}) => ({
  audiobookId: 7,
  operation: SingleBookOperations.pendingMatchSelect,
  status: "completed",
  message: null,
  ...overrides,
});

/** A request the test settles by hand, standing in for the POST that answers 202. */
function deferredRequest() {
  let resolve!: () => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<void>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { request: () => promise, resolve, reject };
}

describe("useSingleBookOperation", () => {
  it("tracks a started book as busy until its completion event arrives, then reports it", async () => {
    const { hub, result, onComplete } = setup();
    const post = deferredRequest();

    let started!: Promise<void>;
    act(() => {
      started = result.current.start(7, post.request);
    });
    expect([...result.current.busyIds]).toEqual([7]);

    await act(async () => {
      post.resolve();
      await started;
    });
    expect([...result.current.busyIds]).toEqual([7]);

    act(() => hub.emit(SignalREvents.SingleBookOperationComplete, completion()));

    expect([...result.current.busyIds]).toEqual([]);
    expect(onComplete).toHaveBeenCalledTimes(1);
    expect(onComplete).toHaveBeenCalledWith(completion());
  });

  // Regression: the server starts the background task before it returns 202, so a fast completion
  // (an immediate failure, say) can reach the page before the POST resolves. The book used to be
  // marked busy only after the POST, which re-added it after the event had removed it - leaving the
  // row permanently busy.
  it("does not leave a book busy when its completion arrives before the request is acknowledged", async () => {
    const { hub, result, onComplete } = setup();
    const post = deferredRequest();

    let started!: Promise<void>;
    act(() => {
      started = result.current.start(7, post.request);
    });
    act(() =>
      hub.emit(
        SignalREvents.SingleBookOperationComplete,
        completion({ status: "failed", message: "Nope" }),
      ),
    );
    expect([...result.current.busyIds]).toEqual([]);

    await act(async () => {
      post.resolve();
      await started;
    });

    expect([...result.current.busyIds]).toEqual([]);
    expect(onComplete).toHaveBeenCalledTimes(1);
  });

  it("un-marks the book and rethrows when the server refuses the request", async () => {
    const { result } = setup();
    const post = deferredRequest();

    let started!: Promise<void>;
    act(() => {
      started = result.current.start(7, post.request);
      // Attached up front so the rejection is never unhandled while the assertion below awaits it.
      void started.catch(() => undefined);
    });
    expect([...result.current.busyIds]).toEqual([7]);

    const failure = new Error("409");
    await act(async () => {
      post.reject(failure);
      await expect(started).rejects.toBe(failure);
    });

    expect([...result.current.busyIds]).toEqual([]);
  });

  it("keeps other books busy when one completes", async () => {
    const { hub, result } = setup();

    await act(async () => {
      await result.current.start(7, () => Promise.resolve());
      await result.current.start(8, () => Promise.resolve());
    });
    act(() => hub.emit(SignalREvents.SingleBookOperationComplete, completion({ audiobookId: 7 })));

    expect([...result.current.busyIds]).toEqual([8]);
  });

  it("ignores another operation's completion for the same book", async () => {
    const { hub, result, onComplete } = setup();

    await act(async () => {
      await result.current.start(7, () => Promise.resolve());
    });
    act(() =>
      hub.emit(
        SignalREvents.SingleBookOperationComplete,
        completion({ operation: SingleBookOperations.pendingRefreshApply }),
      ),
    );

    expect([...result.current.busyIds]).toEqual([7]);
    expect(onComplete).not.toHaveBeenCalled();
  });

  it("clears the busy set and resyncs after a reconnect, since the event may have been missed", async () => {
    const { hub, result, onResync } = setup();

    await act(async () => {
      await result.current.start(7, () => Promise.resolve());
    });
    act(() => hub.reconnect());

    expect([...result.current.busyIds]).toEqual([]);
    expect(onResync).toHaveBeenCalledTimes(1);
  });

  // Regression: the provider's first successful start() (including a retry after the initial one
  // failed) sets isConnected without firing the reconnected callbacks, so an action accepted while
  // the hub was still down lost its event and stayed busy even after the hub connected.
  it("clears the busy set and resyncs when the hub first connects after an action was accepted", async () => {
    const { result, onResync, setConnected } = setup({ connected: false });

    await act(async () => {
      await result.current.start(7, () => Promise.resolve());
    });
    expect([...result.current.busyIds]).toEqual([7]);
    expect(onResync).not.toHaveBeenCalled();

    act(() => setConnected(true));

    expect([...result.current.busyIds]).toEqual([]);
    expect(onResync).toHaveBeenCalledTimes(1);
  });

  it("does not resync just because it mounted while already connected", () => {
    const { onResync } = setup({ connected: true });

    expect(onResync).not.toHaveBeenCalled();
  });
});
