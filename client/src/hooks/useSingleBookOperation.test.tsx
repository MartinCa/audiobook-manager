import type { ReactNode } from "react";
import { describe, it, expect, vi } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { SignalRContext } from "@/context/SignalRContext";
import { SignalREvents, SingleBookOperations } from "@/constants/signalrEvents";
import { createFakeSignalR } from "@/test-utils/fakeSignalR";
import { useSingleBookOperation } from "./useSingleBookOperation";

function setup() {
  const hub = createFakeSignalR();
  const onComplete = vi.fn();
  const onResync = vi.fn();
  const wrapper = ({ children }: { children: ReactNode }) => (
    <SignalRContext.Provider value={hub.value}>{children}</SignalRContext.Provider>
  );
  const { result } = renderHook(
    () => useSingleBookOperation(SingleBookOperations.pendingMatchSelect, onComplete, onResync),
    { wrapper },
  );
  return { hub, result, onComplete, onResync };
}

const completion = (overrides: Record<string, unknown> = {}) => ({
  audiobookId: 7,
  operation: SingleBookOperations.pendingMatchSelect,
  status: "completed",
  message: null,
  ...overrides,
});

describe("useSingleBookOperation", () => {
  it("tracks a started book as busy until its completion event arrives, then reports it", () => {
    const { hub, result, onComplete } = setup();

    act(() => result.current.markStarted(7));
    expect([...result.current.busyIds]).toEqual([7]);

    act(() => hub.emit(SignalREvents.SingleBookOperationComplete, completion()));

    expect([...result.current.busyIds]).toEqual([]);
    expect(onComplete).toHaveBeenCalledTimes(1);
    expect(onComplete).toHaveBeenCalledWith(completion());
  });

  it("keeps other books busy when one completes", () => {
    const { hub, result } = setup();

    act(() => {
      result.current.markStarted(7);
      result.current.markStarted(8);
    });
    act(() => hub.emit(SignalREvents.SingleBookOperationComplete, completion({ audiobookId: 7 })));

    expect([...result.current.busyIds]).toEqual([8]);
  });

  it("ignores another operation's completion for the same book", () => {
    const { hub, result, onComplete } = setup();

    act(() => result.current.markStarted(7));
    act(() =>
      hub.emit(
        SignalREvents.SingleBookOperationComplete,
        completion({ operation: SingleBookOperations.pendingRefreshApply }),
      ),
    );

    expect([...result.current.busyIds]).toEqual([7]);
    expect(onComplete).not.toHaveBeenCalled();
  });

  it("clears the busy set and resyncs after a reconnect, since the event may have been missed", () => {
    const { hub, result, onResync } = setup();

    act(() => result.current.markStarted(7));
    act(() => hub.reconnect());

    expect([...result.current.busyIds]).toEqual([]);
    expect(onResync).toHaveBeenCalledTimes(1);
  });
});
