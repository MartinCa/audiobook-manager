import {
  HubConnection,
  HubConnectionBuilder,
  LogLevel,
} from "@microsoft/signalr";
import { App, inject, InjectionKey, onMounted, onUnmounted } from "vue";

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

// Registers a hub event listener for exactly the calling component's lifetime, pairing
// on()/off() automatically. Prefer this over calling useSignalR().on/off directly - a raw
// on() with no matching off() in onUnmounted leaks a listener per mount/unmount cycle for the
// rest of the SPA session, and nothing else guards against forgetting the pairing.
export function useSignalREvent<T>(
  token: HubEventToken<T>,
  callback: (payload: T) => void,
): void {
  const client = useSignalR();
  onMounted(() => client.on(token, callback));
  onUnmounted(() => client.off(token, callback));
}

// Same pairing guarantee as useSignalREvent, for the reconnect callback (which has no token
// and lives in its own listener set - see SignalRClient.onReconnected above).
export function useSignalRReconnected(callback: () => void): void {
  const client = useSignalR();
  onMounted(() => client.onReconnected(callback));
  onUnmounted(() => client.offReconnected(callback));
}
