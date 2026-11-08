import { vi } from "vitest";
import type { HubEventHandler, SignalRContextValue } from "@/context/SignalRContext";

/**
 * A SignalRContext value for tests that can be driven by hand: `emit` delivers an event to every
 * handler registered for it (the way the hub would), `reconnect` fires the reconnected callbacks.
 */
export function createFakeSignalR() {
  const handlers = new Map<string, Set<HubEventHandler<unknown>>>();
  const reconnectCallbacks = new Set<() => void>();

  const value: SignalRContextValue = {
    connection: null,
    isConnected: true,
    on: vi.fn(<T>(eventName: string, handler: HubEventHandler<T>) => {
      const set = handlers.get(eventName) ?? new Set();
      set.add(handler as HubEventHandler<unknown>);
      handlers.set(eventName, set);
    }) as SignalRContextValue["on"],
    off: vi.fn(<T>(eventName: string, handler: HubEventHandler<T>) => {
      handlers.get(eventName)?.delete(handler as HubEventHandler<unknown>);
    }) as SignalRContextValue["off"],
    onReconnected: vi.fn((callback: () => void) => {
      reconnectCallbacks.add(callback);
    }),
    offReconnected: vi.fn((callback: () => void) => {
      reconnectCallbacks.delete(callback);
    }),
  };

  return {
    value,
    emit: (eventName: string, data: unknown) => {
      for (const handler of handlers.get(eventName) ?? []) handler(data);
    },
    reconnect: () => {
      for (const callback of reconnectCallbacks) callback();
    },
  };
}
