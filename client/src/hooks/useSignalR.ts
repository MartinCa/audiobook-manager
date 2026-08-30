import { useEffect, useRef, useState } from "react";
import * as signalR from "@microsoft/signalr";

export function useSignalR(hubUrl: string = "/hubs/organize") {
  const [connection, setConnection] = useState<signalR.HubConnection | null>(null);
  const [isConnected, setIsConnected] = useState(false);
  const listenersRef = useRef<Map<string, Set<(data: any) => void>>>(new Map());

  useEffect(() => {
    const newConnection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect()
      .build();

    newConnection.onreconnected(() => {
      setIsConnected(true);
    });

    newConnection.onclose(() => {
      setIsConnected(false);
    });

    newConnection
      .start()
      .then(() => {
        setIsConnected(true);
        setConnection(newConnection);

        // Re-attach existing registered listeners
        listenersRef.current.forEach((handlers, eventName) => {
          handlers.forEach((handler) => {
            newConnection.on(eventName, handler);
          });
        });
      })
      .catch((err) => {
        console.error("SignalR Connection Error: ", err);
      });

    return () => {
      newConnection.stop();
    };
  }, [hubUrl]);

  const subscribe = <T>(eventName: string, handler: (data: T) => void) => {
    if (!listenersRef.current.has(eventName)) {
      listenersRef.current.set(eventName, new Set());
    }
    listenersRef.current.get(eventName)!.add(handler);

    if (connection && isConnected) {
      connection.on(eventName, handler);
    }

    return () => {
      listenersRef.current.get(eventName)?.delete(handler);
      if (connection) {
        connection.off(eventName, handler);
      }
    };
  };

  return { connection, isConnected, subscribe };
}
