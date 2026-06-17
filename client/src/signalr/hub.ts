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
}

const key: InjectionKey<SignalRClient> = Symbol("signalr");

export function createSignalR(url: string) {
  let connection: HubConnection;

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

      const client: SignalRClient = {
        on<T>(token: HubEventToken<T>, callback: (payload: T) => void) {
          connection.on(token as string, callback);
        },
        off<T>(token: HubEventToken<T>, callback: (payload: T) => void) {
          connection.off(token as string, callback);
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
