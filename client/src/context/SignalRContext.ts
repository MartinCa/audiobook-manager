import { createContext } from "react";
import type { HubConnection } from "@microsoft/signalr";

export type HubEventHandler<T> = (data: T) => void;

export interface SignalRContextValue {
  connection: HubConnection | null;
  isConnected: boolean;
  on: <T>(eventName: string, handler: HubEventHandler<T>) => void;
  off: <T>(eventName: string, handler: HubEventHandler<T>) => void;
  onReconnected: (callback: () => void) => void;
  offReconnected: (callback: () => void) => void;
}

export const SignalRContext = createContext<SignalRContextValue | null>(null);
