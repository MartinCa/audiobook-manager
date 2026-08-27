import {
  HubConnection,
  HubConnectionBuilder,
  LogLevel,
} from "@microsoft/signalr";
import { App, inject, InjectionKey } from "vue";

export type HubEventToken<T> = string & { __type?: T };

interface SignalRClient {
  on<T>(token: HubEventToken<T>, callback: (payload: T) => void): void;
  off<T>(token: HubEventToken<T>, callback: (payload: T) => void): void;
  // Fires after the connection drops and is automatically re-established (not on the
  // initial connect). Progress/complete events broadcast while disconnected are lost, so
  // listeners use this to re-fetch authoritative state instead of trusting stale local state.
  onReconnected(callback: () => void): void;
  offReconnected(callback: () => void): void;
}

const key: InjectionKey<SignalRClient> = Symbol("signalr");

export function createSignalR(url: string) {
  let connection: HubConnection;
  const reconnectedListeners = new Set<() => void>();

  return {
    install(app: App) {
      connection = new HubConnectionBuilder()
        .withUrl(url)
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Warning)
        .build();

      connection
        .start()
        .catch((err) => console.error("SignalR connection error:", err));

      // HubConnection has no off-equivalent for onreconnected, so listeners are tracked
      // separately and fanned out from a single subscription.
      connection.onreconnected(() => {
        reconnectedListeners.forEach((callback) => callback());
      });

      const client: SignalRClient = {
        on<T>(token: HubEventToken<T>, callback: (payload: T) => void) {
          connection.on(token as string, callback);
        },
        off<T>(token: HubEventToken<T>, callback: (payload: T) => void) {
          connection.off(token as string, callback);
        },
        onReconnected(callback: () => void) {
          reconnectedListeners.add(callback);
        },
        offReconnected(callback: () => void) {
          reconnectedListeners.delete(callback);
        },
      };

      app.provide(key, client);
    },
  };
}

export function useSignalR(): SignalRClient {
  const client = inject(key);
  if (!client) throw new Error("SignalR plugin not installed");
  return client;
}
