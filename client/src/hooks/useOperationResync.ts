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

  useEffect(() => {
    onStatusRef.current = onStatus;
  });

  const refresh = (): void => {
    operationsApi
      .getStatus(key)
      .then((status) => onStatusRef.current(status))
      .catch(() => {
        // Leave current state as-is if the status check itself fails.
      });
  };

  useEffect(() => {
    refresh();
    // Only re-run when the operation key itself changes; `refresh` closes over a ref.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);

  useSignalRReconnected(refresh);
}
