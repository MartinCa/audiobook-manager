import { describe, it, expect, vi, beforeEach } from "vitest";
import { StrictMode } from "react";
import { render, screen, act, waitFor } from "@testing-library/react";
import type { Mock } from "vitest";
import { SignalRProvider, START_RETRY_BASE_DELAY_MS } from "./SignalRProvider";
import { SignalREvents } from "@/constants/signalrEvents";
import { useSignalREvent, useSignalRReconnected } from "@/hooks/useSignalR";

type SignalRCallback = (...args: unknown[]) => void;

interface MockHubConnection {
  on: ReturnType<typeof vi.fn>;
  off: ReturnType<typeof vi.fn>;
  onreconnected: ReturnType<typeof vi.fn>;
  onclose: ReturnType<typeof vi.fn>;
  start: Mock<() => Promise<void>>;
  stop: ReturnType<typeof vi.fn>;
  /** Settles the promise THIS connection's start() returned (per-connection, so a test can
   *  resolve or reject a stale connection's start independently of the live one's). */
  resolveStart: (() => void) | undefined;
  rejectStart: ((err: unknown) => void) | undefined;
  /** The handler this connection's onreconnected registered (for the reconnect test). */
  reconnectedHandler: (() => void) | undefined;
  /** The handler registered for an event name on THIS connection. */
  getHandler: (eventName: string) => SignalRCallback | undefined;
}

/** Every connection the mocked builder has handed out, oldest first — the StrictMode race tests
 *  need to address the first (torn-down) connection after the provider remounts. */
let mockConnections: MockHubConnection[] = [];
let mockHubConnection: MockHubConnection;
let mockOnHandlers: Record<string, SignalRCallback> = {};
let mockReconnectedHandler: (() => void) | null = null;
let startPromiseResolve: () => void;
let startPromiseReject: (err: unknown) => void;

function createMockConnection(): MockHubConnection {
  const handlers: Record<string, SignalRCallback> = {};
  const connection: MockHubConnection = {
    on: vi.fn(),
    off: vi.fn(),
    onreconnected: vi.fn(),
    onclose: vi.fn(),
    // Typed as a Promise-returning mock so the implementation below may actually return the
    // pending promise (the default `vi.fn()` mock types the callback as void-returning).
    start: vi.fn<() => Promise<void>>(),
    stop: vi.fn(() => Promise.resolve()),
    resolveStart: undefined,
    rejectStart: undefined,
    reconnectedHandler: undefined,
    getHandler: (eventName: string) => handlers[eventName],
  };
  connection.on.mockImplementation((eventName: string, handler: SignalRCallback) => {
    handlers[eventName] = handler;
  });
  connection.off.mockImplementation((eventName: string) => {
    delete handlers[eventName];
  });
  connection.onreconnected.mockImplementation((handler: () => void) => {
    connection.reconnectedHandler = handler;
    mockReconnectedHandler = handler;
  });
  connection.start.mockImplementation(() => {
    return new Promise<void>((resolve, reject) => {
      connection.resolveStart = resolve;
      connection.rejectStart = reject;
      startPromiseResolve = resolve;
      startPromiseReject = reject;
    });
  });
  // The module-level aliases point at the most recently built connection, so single-render
  // tests keep addressing "the" connection exactly as before.
  mockOnHandlers = handlers;
  mockConnections.push(connection);
  mockHubConnection = connection;
  return connection;
}

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
        return createMockConnection();
      }
    },
    LogLevel: { Warning: 2 },
  };
});

describe("SignalRProvider", () => {
  beforeEach(() => {
    mockConnections = [];
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

  // Regression for the StrictMode double-mount race (review of the provider's retry change): a
  // StrictMode mount runs the effect setup -> cleanup -> setup twice on the SAME component
  // instance, and the cleanup of the first connection's effect used to flip a shared `disposed`
  // ref that the second effect's setup then reset. When the FIRST connection's start() settled
  // after that (resolve or reject), it was treated as live: its .then() re-bound every event name
  // onto the dead connection (and, because the shared bound-events set was then full, the live
  // connection never bound them at all), and a rejection scheduled retries against a connection
  // that no longer existed. The liveness check must belong to the effect instance, not to the
  // component. Rendering under StrictMode is what reproduces the same-instance double effect.
  it("never lets a torn-down effect's start resolve into the remounted live connection", async () => {
    vi.useFakeTimers();
    try {
      render(
        <StrictMode>
          <SignalRProvider>
            <div />
          </SignalRProvider>
        </StrictMode>,
      );

      // StrictMode built two connections; the first effect's start() is still in flight.
      const staleConnection = mockConnections[0];
      const liveConnection = mockConnections[1];
      expect(staleConnection.start).toHaveBeenCalledTimes(1);
      expect(liveConnection.start).toHaveBeenCalledTimes(1);

      // The stale start() settles successfully on the dead connection. With the shared-flag bug
      // it re-bound every event onto the stale connection (starving the live one of bindings);
      // with the per-effect token it is a no-op.
      act(() => {
        staleConnection.resolveStart?.();
      });
      for (let i = 0; i < 5; i++) {
        await Promise.resolve();
      }
      expect(staleConnection.on).not.toHaveBeenCalled();

      // The live connection's own start still drives the provider normally.
      act(() => {
        liveConnection.resolveStart?.();
      });
      for (let i = 0; i < 5; i++) {
        await Promise.resolve();
      }
      expect(liveConnection.on).toHaveBeenCalledWith(
        SignalREvents.UpdateProgress,
        expect.any(Function),
      );
      expect(staleConnection.on).not.toHaveBeenCalled();
    } finally {
      vi.useRealTimers();
    }
  });

  it("never schedules retries for a connection whose effect was torn down", async () => {
    vi.useFakeTimers();
    try {
      render(
        <StrictMode>
          <SignalRProvider>
            <div />
          </SignalRProvider>
        </StrictMode>,
      );
      const staleConnection = mockConnections[0];
      const liveConnection = mockConnections[1];

      // The stale start() fails after its effect was torn down. The shared-flag bug saw the
      // second setup's reset `disposed` and scheduled a retry for the dead connection; the
      // per-effect token must not.
      act(() => {
        staleConnection.rejectStart?.(new Error("connection refused"));
      });
      for (let i = 0; i < 5; i++) {
        await Promise.resolve();
      }
      const staleTimers = vi.getTimerCount ? vi.getTimerCount() : -1;
      expect(
        staleTimers,
        `no retry timer should exist for the dead connection, saw ${staleTimers}`,
      ).toBe(0);

      // The live connection's retry behavior is unaffected.
      act(() => {
        liveConnection.rejectStart?.(new Error("connection refused"));
      });
      for (let i = 0; i < 5; i++) {
        await Promise.resolve();
      }
      expect(vi.getTimerCount()).toBe(1);

      act(() => {
        vi.advanceTimersByTime(START_RETRY_BASE_DELAY_MS);
      });
      expect(liveConnection.start).toHaveBeenCalledTimes(2);
      expect(staleConnection.start).toHaveBeenCalledTimes(1);
    } finally {
      vi.useRealTimers();
    }
  });
});
