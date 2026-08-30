import { useContext, useEffect, useRef } from "react";
import {
  SignalRContext,
  type SignalRContextValue,
  type HubEventHandler,
} from "@/context/SignalRContext";

export function useSignalR(): SignalRContextValue {
  const ctx = useContext(SignalRContext);
  if (!ctx) {
    throw new Error("useSignalR must be used within a SignalRProvider");
  }
  return ctx;
}

export function useSignalREvent<T>(eventName: string, handler: HubEventHandler<T>): void {
  const signalR = useSignalR();
  const handlerRef = useRef(handler);

  useEffect(() => {
    handlerRef.current = handler;
  });

  useEffect(() => {
    const listener: HubEventHandler<T> = (data) => handlerRef.current(data);
    signalR.on(eventName, listener);
    return () => {
      signalR.off(eventName, listener);
    };
  }, [signalR, eventName]);
}

export function useSignalRReconnected(callback: () => void): void {
  const signalR = useSignalR();
  const callbackRef = useRef(callback);

  useEffect(() => {
    callbackRef.current = callback;
  });

  useEffect(() => {
    const listener = () => callbackRef.current();
    signalR.onReconnected(listener);
    return () => {
      signalR.offReconnected(listener);
    };
  }, [signalR]);
}
