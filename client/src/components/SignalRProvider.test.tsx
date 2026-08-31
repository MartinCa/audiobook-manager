import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, act, waitFor } from "@testing-library/react";
import { SignalRProvider } from "./SignalRProvider";
import { useSignalREvent, useSignalRReconnected } from "@/hooks/useSignalR";

type SignalRCallback = (...args: unknown[]) => void;

let mockOnHandlers: Record<string, SignalRCallback> = {};
let mockReconnectedHandler: (() => void) | null = null;
let startPromiseResolve: () => void;

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
    return new Promise<void>((resolve) => {
      startPromiseResolve = resolve;
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
        <TestSubscriber eventName="UpdateProgress" onMessage={messageHandler} />
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
      expect(mockHubConnection.on).toHaveBeenCalledWith("UpdateProgress", expect.any(Function));
    });

    // Simulate backend sending an event
    act(() => {
      mockOnHandlers["UpdateProgress"]?.({
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
        <TestSubscriber eventName="UpdateProgress" onMessage={handler1} />
        <TestSubscriber eventName="UpdateProgress" onMessage={handler2} />
      </SignalRProvider>,
    );

    act(() => {
      startPromiseResolve();
    });

    await waitFor(() => {
      expect(mockHubConnection.on).toHaveBeenCalledWith("UpdateProgress", expect.any(Function));
    });

    act(() => {
      mockOnHandlers["UpdateProgress"]?.({ progress: 50 });
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
});
