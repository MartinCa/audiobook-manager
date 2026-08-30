import { useEffect, useRef, useState, type ReactNode } from "react";
import { HubConnectionBuilder, LogLevel, type HubConnection } from "@microsoft/signalr";
import { SignalRContext, type HubEventHandler } from "@/context/SignalRContext";

export function SignalRProvider({
  children,
  url = "/hubs/organize",
}: {
  children: ReactNode;
  url?: string;
}) {
  const [connection, setConnection] = useState<HubConnection | null>(null);
  const [isConnected, setIsConnected] = useState(false);
  const reconnectedListeners = useRef<Set<() => void>>(new Set());
  const eventListeners = useRef<Map<string, Set<HubEventHandler<unknown>>>>(new Map());

  useEffect(() => {
    const hubConnection = new HubConnectionBuilder()
      .withUrl(url)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    hubConnection.onreconnected(() => {
      setIsConnected(true);
      reconnectedListeners.current.forEach((cb) => cb());
    });

    hubConnection.onclose(() => {
      setIsConnected(false);
    });

    void hubConnection
      .start()
      .then(() => {
        setIsConnected(true);
        setConnection(hubConnection);
      })
      .catch((err: unknown) => {
        console.warn("SignalR connection error:", err);
      });

    return () => {
      void hubConnection.stop();
    };
  }, [url]);

  const on = <T,>(eventName: string, handler: HubEventHandler<T>) => {
    let handlers = eventListeners.current.get(eventName);
    if (!handlers) {
      handlers = new Set();
      eventListeners.current.set(eventName, handlers);
    }
    const genericHandler = handler as HubEventHandler<unknown>;
    handlers.add(genericHandler);

    if (connection) {
      connection.on(eventName, genericHandler);
    }
  };

  const off = <T,>(eventName: string, handler: HubEventHandler<T>) => {
    const handlers = eventListeners.current.get(eventName);
    const genericHandler = handler as HubEventHandler<unknown>;
    if (handlers) {
      handlers.delete(genericHandler);
    }
    if (connection) {
      connection.off(eventName, genericHandler);
    }
  };

  const onReconnected = (callback: () => void) => {
    reconnectedListeners.current.add(callback);
  };

  const offReconnected = (callback: () => void) => {
    reconnectedListeners.current.delete(callback);
  };

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
