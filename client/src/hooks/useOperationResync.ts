import { useEffect, useRef } from "react";
import { operationsApi } from "@/services/api";
import { useSignalRReconnected } from "@/hooks/useSignalR";
import type { OperationStatus } from "@/types/OperationStatus";

/**
 * Recovers the current state of a background operation identified by `key` (see
 * IOperationStatusRegistry server-side) on mount and after a SignalR reconnect, so a
 * page opened (or reopened after a dropped connection) while the operation is already
 * running server-side reflects that instead of looking idle until the next event.
 *
 * @param resyncTrigger An optional value whose change re-runs the mount-time status fetch.
 *   A permanently-mounted component (a dialog that only toggles its portal) never remounts
 *   when it becomes visible, so its mount-time fetch happens once at page load and a status
 *   that changed since - an apply started elsewhere - would go unnoticed. Pass a value that
 *   changes each time the component's relevant window becomes visible (e.g. a counter bumped
 *   on the dialog's open transition) and the hook re-fetches on that transition too. The
 *   cleanup of the previous effect instance discards an in-flight response from the earlier
 *   window, so two fetches racing each other cannot both apply.
 */
export function useOperationResync(
  key: string,
  onStatus: (status: OperationStatus) => void,
  resyncTrigger?: unknown,
): void {
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
  }, [key, resyncTrigger]);

  useSignalRReconnected(refresh);
}
