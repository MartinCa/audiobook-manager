import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, act, waitFor } from "@testing-library/react";
import { SignalRProvider, START_RETRY_BASE_DELAY_MS } from "./SignalRProvider";
import { SignalREvents } from "@/constants/signalrEvents";
import { useSignalREvent, useSignalRReconnected } from "@/hooks/useSignalR";

type SignalRCallback = (...args: unknown[]) => void;

let mockOnHandlers: Record<string, SignalRCallback> = {};
let mockReconnectedHandler: (() => void) | null = null;
let startPromiseResolve: () => void;
let startPromiseReject: (err: unknown) => void;

const mockHubConnection = {
  on: vi.fn((eventName: string, handler: SignalRCallback) => {
    mockOnHandlers[eventName] = handler;
  }),
  off: vi.fn((eventName: string) => {
    delete mockOnHandlers[eventName];
  }),
  onreconnected: vi.fn((handler: () => void) => {
    mockReconnectedHandler = handler;
  }),
  onclose: vi.fn(),
  start: vi.fn(() => {
    return new Promise<void>((resolve, reject) => {
      startPromiseResolve = resolve;
      startPromiseReject = reject;
    });
  }),
  stop: vi.fn(() => Promise.resolve()),
};

vi.mock("@microsoft/signalr", () => {
  return {
    HubConnectionBuilder: class {
      withUrl() {
        return this;
      }
      withAutomaticReconnect() {
        return this;
      }
      configureLogging() {
        return this;
      }
      build() {
        return mockHubConnection;
      }
    },
    LogLevel: { Warning: 2 },
  };
});

describe("SignalRProvider", () => {
  beforeEach(() => {
    mockOnHandlers = {};
    mockReconnectedHandler = null;
    vi.clearAllMocks();
  });

  function TestSubscriber({
    eventName,
    onMessage,
  }: {
    eventName: string;
    onMessage: (data: unknown) => void;
  }) {
    useSignalREvent(eventName, onMessage);
    return <div>Subscriber for {eventName}</div>;
  }

  function ReconnectSubscriber({ onReconnected }: { onReconnected: () => void }) {
    useSignalRReconnected(onReconnected);
    return <div>Reconnect Subscriber</div>;
  }

  it("dispatches events to subscribers registered before start() resolves", async () => {
    const messageHandler = vi.fn();

    render(
      <SignalRProvider>
        <TestSubscriber eventName={SignalREvents.UpdateProgress} onMessage={messageHandler} />
      </SignalRProvider>,
    );

    expect(screen.getByText("Subscriber for UpdateProgress")).toBeInTheDocument();

    // Connection is still pending start()
    expect(mockHubConnection.start).toHaveBeenCalled();

    // Now resolve start()
    act(() => {
      startPromiseResolve();
    });

    // The handler should now be bound on the hub connection
    await waitFor(() => {
      expect(mockHubConnection.on).toHaveBeenCalledWith(
        SignalREvents.UpdateProgress,
        expect.any(Function),
      );
    });

    // Simulate backend sending an event
    act(() => {
      mockOnHandlers[SignalREvents.UpdateProgress]?.({
        originalFileLocation: "/path/book.m4b",
        progress: 38,
        progressMessage: "Saving tags",
      });
    });

    expect(messageHandler).toHaveBeenCalledWith({
      originalFileLocation: "/path/book.m4b",
      progress: 38,
      progressMessage: "Saving tags",
    });
  });

  it("supports multiple subscribers on the same event and handles unmounting", async () => {
    const handler1 = vi.fn();
    const handler2 = vi.fn();

    const { unmount } = render(
      <SignalRProvider>
        <TestSubscriber eventName={SignalREvents.UpdateProgress} onMessage={handler1} />
        <TestSubscriber eventName={SignalREvents.UpdateProgress} onMessage={handler2} />
      </SignalRProvider>,
    );

    act(() => {
      startPromiseResolve();
    });

    await waitFor(() => {
      expect(mockHubConnection.on).toHaveBeenCalledWith(
        SignalREvents.UpdateProgress,
        expect.any(Function),
      );
    });

    act(() => {
      mockOnHandlers[SignalREvents.UpdateProgress]?.({ progress: 50 });
    });

    expect(handler1).toHaveBeenCalledWith({ progress: 50 });
    expect(handler2).toHaveBeenCalledWith({ progress: 50 });

    unmount();
  });

  it("triggers onReconnected listeners when SignalR reconnects", async () => {
    const reconnectedCallback = vi.fn();

    render(
      <SignalRProvider>
        <ReconnectSubscriber onReconnected={reconnectedCallback} />
      </SignalRProvider>,
    );

    act(() => {
      startPromiseResolve();
    });

    await waitFor(() => {
      expect(mockReconnectedHandler).toBeDefined();
    });

    act(() => {
      mockReconnectedHandler?.();
    });

    expect(reconnectedCallback).toHaveBeenCalledTimes(1);
  });

  // Guard for the reported consistency-check progress warning: the backend broadcasts progress/
  // completion events regardless of which page is mounted (a full-library check started from the
  // library page still emits ConsistencyCheckProgress while no consistency page is open), and
  // @microsoft/signalr logs a console warning when an event arrives with no client handler bound.
  // The provider must pre-bind every parity-test-known event name at the connection so the whole
  // backend surface is always handled (no-op when no listener is registered), instead of only the
  // events of the currently mounted page.
  it("pre-binds every backend event name so unhandled broadcasts do not warn", async () => {
    render(
      <SignalRProvider>
        <div />
      </SignalRProvider>,
    );

    act(() => {
      startPromiseResolve();
    });

    // The reported event itself must be bound even though no subscriber exists for it.
    await waitFor(() => {
      expect(mockHubConnection.on).toHaveBeenCalledWith(
        SignalREvents.ConsistencyCheckProgress,
        expect.any(Function),
      );
    });

    for (const eventName of Object.values(SignalREvents)) {
      expect(mockHubConnection.on).toHaveBeenCalledWith(eventName, expect.any(Function));
    }
  });

  it("dispatches a pre-bound event to a handler registered after the connection started", async () => {
    const messageHandler = vi.fn();

    const { rerender } = render(
      <SignalRProvider>
        <div />
      </SignalRProvider>,
    );

    act(() => {
      startPromiseResolve();
    });

    // The provider pre-bound the event eagerly, before any subscriber existed.
    await waitFor(() => {
      expect(mockHubConnection.on).toHaveBeenCalledWith(
        SignalREvents.UpdateProgress,
        expect.any(Function),
      );
    });

    // A subscriber mounts after start: the binding is already in place, and the dispatcher must
    // route the broadcast through to the newly registered listener.
    rerender(
      <SignalRProvider>
        <TestSubscriber eventName={SignalREvents.UpdateProgress} onMessage={messageHandler} />
      </SignalRProvider>,
    );

    act(() => {
      mockOnHandlers[SignalREvents.UpdateProgress]?.({
        originalFileLocation: "/path/book.m4b",
        progress: 10,
        progressMessage: "Working",
      });
    });

    expect(messageHandler).toHaveBeenCalledWith({
      originalFileLocation: "/path/book.m4b",
      progress: 10,
      progressMessage: "Working",
    });
  });

  it("retries the initial start with backoff when it fails, then connects on success", async () => {
    vi.useFakeTimers();
    try {
      render(
        <SignalRProvider>
          <div />
        </SignalRProvider>,
      );

      expect(mockHubConnection.start).toHaveBeenCalledTimes(1);

      // First start fails: the provider must schedule a retry, not give up silently. The await
      // drains the rejected promise's microtask so the catch has actually scheduled the retry
      // timer before the clock advances.
      act(() => {
        startPromiseReject(new Error("connection refused"));
      });
      // Flush the promise chain with several yields: the rejection hops through .then and .catch
      // continuations, each of which needs its own microtask to run.
      for (let i = 0; i < 5; i++) {
        await Promise.resolve();
      }
      const timersNow = vi.getTimerCount ? vi.getTimerCount() : -1;
      expect(timersNow, `one retry timer should be scheduled, saw ${timersNow}`).toBe(1);
      expect(mockHubConnection.start).toHaveBeenCalledTimes(1);

      // The retry fires after the base backoff delay.
      act(() => {
        vi.advanceTimersByTime(START_RETRY_BASE_DELAY_MS);
      });
      expect(mockHubConnection.start).toHaveBeenCalledTimes(2);

      // Second attempt succeeds: events are bound on the live connection. A real `await` still
      // drains the promise chain under fake timers (only the clock is faked), and act() wraps
      // the resolve so React's state updates flush with it.
      act(() => {
        startPromiseResolve();
      });
      for (let i = 0; i < 5; i++) {
        await Promise.resolve();
      }
      expect(mockHubConnection.on).toHaveBeenCalledWith(
        SignalREvents.UpdateProgress,
        expect.any(Function),
      );
    } finally {
      vi.useRealTimers();
    }
  });
});
