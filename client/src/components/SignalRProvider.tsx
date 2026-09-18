import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import { HubConnectionBuilder, LogLevel, type HubConnection } from "@microsoft/signalr";
import { SignalRContext, type HubEventHandler } from "@/context/SignalRContext";
import { SignalREvents } from "@/constants/signalrEvents";

// @microsoft/signalr's automatic reconnect only kicks in after a connection has been
// established once. A failed *initial* start (the API not up yet, a proxy hiccup) is a
// terminal error that otherwise leaves the client permanently disconnected with no event ever
// arriving - a bulk edit started through the HTTP API would run server-side while the UI sat
// idle. The provider retries the initial start with an exponential backoff instead.
export const START_RETRY_BASE_DELAY_MS = 2_000;
export const START_RETRY_MAX_DELAY_MS = 30_000;

export function SignalRProvider({
  children,
  url = "/hubs/organize",
}: {
  children: ReactNode;
  url?: string;
}) {
  const [connection, setConnection] = useState<HubConnection | null>(null);
  const [isConnected, setIsConnected] = useState(false);
  const connectionRef = useRef<HubConnection | null>(null);
  const reconnectedListeners = useRef<Set<() => void>>(new Set());
  const eventListeners = useRef<Map<string, Set<HubEventHandler<unknown>>>>(new Map());
  const boundEvents = useRef<Set<string>>(new Set());
  const retryTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const disposed = useRef(false);

  const bindEventToConnection = useCallback((conn: HubConnection, eventName: string) => {
    if (!boundEvents.current.has(eventName)) {
      boundEvents.current.add(eventName);
      conn.on(eventName, (data: unknown) => {
        const handlers = eventListeners.current.get(eventName);
        if (handlers) {
          handlers.forEach((handler) => {
            try {
              handler(data);
            } catch (err) {
              console.error(`Error in SignalR listener for ${eventName}:`, err);
            }
          });
        }
      });
    }
  }, []);

  useEffect(() => {
    const hubConnection = new HubConnectionBuilder()
      .withUrl(url)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    const currentBoundEvents = boundEvents.current;
    connectionRef.current = hubConnection;
    currentBoundEvents.clear();
    disposed.current = false;

    hubConnection.onreconnected(() => {
      setIsConnected(true);
      reconnectedListeners.current.forEach((cb) => {
        try {
          cb();
        } catch (err) {
          console.error("Error in SignalR reconnected listener:", err);
        }
      });
    });

    hubConnection.onclose(() => {
      setIsConnected(false);
    });

    const bindAllEventNames = (): void => {
      // Bind every event name the backend can publish - not just the ones a component has
      // registered a listener for - plus any listeners registered before start resolved. The
      // binding dispatches through the listener map, so an event with no current listeners is
      // a harmless no-op, while @microsoft/signalr's client never logs its "no client method
      // with the name ... found" warning: the backend broadcasts progress/completion
      // (e.g. ConsistencyCheckProgress) regardless of which page a user is on, and pages that
      // do not render the matching feature must not turn a background operation elsewhere into
      // a console warning. This only covers the parity-test-known surface (signalrEvents.ts);
      // a version-skewed backend publishing an event this client build does not know would
      // still warn, which the SignalREventParityTests guard against at build time.
      for (const eventName of Object.values(SignalREvents)) {
        bindEventToConnection(hubConnection, eventName);
      }
      for (const eventName of eventListeners.current.keys()) {
        bindEventToConnection(hubConnection, eventName);
      }
    };

    const scheduleRetry = (attempt: number): void => {
      const delay = Math.min(
        START_RETRY_BASE_DELAY_MS * 2 ** (attempt - 1),
        START_RETRY_MAX_DELAY_MS,
      );
      retryTimer.current = setTimeout(() => {
        retryTimer.current = null;
        attemptStart(attempt + 1);
      }, delay);
    };

    function attemptStart(attempt: number): void {
      if (disposed.current) {
        return;
      }

      void hubConnection
        .start()
        .then(() => {
          if (disposed.current) {
            return;
          }
          setIsConnected(true);
          setConnection(hubConnection);
          bindAllEventNames();
        })
        .catch((err: unknown) => {
          console.warn(`SignalR connection error (attempt ${attempt}):`, err);
          if (!disposed.current) {
            scheduleRetry(attempt);
          }
        });
    }

    attemptStart(1);

    return () => {
      disposed.current = true;
      if (retryTimer.current !== null) {
        clearTimeout(retryTimer.current);
        retryTimer.current = null;
      }
      connectionRef.current = null;
      currentBoundEvents.clear();
      void hubConnection.stop();
    };
  }, [url, bindEventToConnection]);

  const on = useCallback(
    <T,>(eventName: string, handler: HubEventHandler<T>) => {
      let handlers = eventListeners.current.get(eventName);
      if (!handlers) {
        handlers = new Set();
        eventListeners.current.set(eventName, handlers);
      }
      handlers.add(handler as HubEventHandler<unknown>);

      if (connectionRef.current) {
        bindEventToConnection(connectionRef.current, eventName);
      }
    },
    [bindEventToConnection],
  );

  const off = useCallback(<T,>(eventName: string, handler: HubEventHandler<T>) => {
    const handlers = eventListeners.current.get(eventName);
    if (handlers) {
      handlers.delete(handler as HubEventHandler<unknown>);
      if (handlers.size === 0) {
        eventListeners.current.delete(eventName);
      }
    }
  }, []);

  const onReconnected = useCallback((callback: () => void) => {
    reconnectedListeners.current.add(callback);
  }, []);

  const offReconnected = useCallback((callback: () => void) => {
    reconnectedListeners.current.delete(callback);
  }, []);

  return (
    <SignalRContext.Provider
      value={{
        connection,
        isConnected,
        on,
        off,
        onReconnected,
        offReconnected,
      }}
    >
      {children}
    </SignalRContext.Provider>
  );
}

export default SignalRProvider;
