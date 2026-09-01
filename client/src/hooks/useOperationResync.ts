import { useEffect, useRef } from "react";
import { operationsApi } from "@/services/api";
import { useSignalRReconnected } from "@/hooks/useSignalR";
import type { OperationStatus } from "@/types/OperationStatus";

/**
 * Recovers the current state of a background operation identified by `key` (see
 * IOperationStatusRegistry server-side) on mount and after a SignalR reconnect, so a
 * page opened (or reopened after a dropped connection) while the operation is already
 * running server-side reflects that instead of looking idle until the next event.
 */
export function useOperationResync(key: string, onStatus: (status: OperationStatus) => void): void {
  const onStatusRef = useRef(onStatus);
  const keyRef = useRef(key);

  useEffect(() => {
    onStatusRef.current = onStatus;
  });

  useEffect(() => {
    keyRef.current = key;
  }, [key]);

  const refresh = (): void => {
    const requestedKey = keyRef.current;
    operationsApi
      .getStatus(requestedKey)
      .then((status) => {
        if (keyRef.current === requestedKey) {
          onStatusRef.current(status);
        }
      })
      .catch(() => {
        // Leave current state as-is if the status check itself fails.
      });
  };

  useEffect(() => {
    let mounted = true;
    operationsApi
      .getStatus(key)
      .then((status) => {
        if (mounted && keyRef.current === key) {
          onStatusRef.current(status);
        }
      })
      .catch(() => {});

    return () => {
      mounted = false;
    };
  }, [key]);

  useSignalRReconnected(refresh);
}
