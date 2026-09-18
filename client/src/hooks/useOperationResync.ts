import { useCallback, useEffect, useRef } from "react";
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
 *   on the dialog's open transition) and the hook re-fetches on that transition too.
 *
 * @returns An `invalidate` function: call it from the operation's SignalR event handlers
 *   (progress AND completion) before touching any state. A status response is a snapshot of
 *   the operation registry from the moment the request was answered; a SignalR event that
 *   arrives while that request is in flight is newer truth and must win. `invalidate` marks
 *   every response fetched earlier as stale and discards it when it resolves, which is what
 *   stops a stale `isRunning: false` from clearing a live progress bar (an event set it
 *   during the fetch) and a delayed `isRunning: true` from resurrecting one after the
 *   completion event cleared it. Responses are also ordered by request sequence, so when
 *   several fetches are in flight together (a reconnect racing the mount fetch, or a
 *   re-open racing a reconnect) only the newest one's response is ever applied.
 */
export function useOperationResync(
  key: string,
  onStatus: (status: OperationStatus) => void,
  resyncTrigger?: unknown,
): () => void {
  const onStatusRef = useRef(onStatus);
  const keyRef = useRef(key);
  // Monotonic fetch id: a response applies only while it is still the newest fetch, so a
  // slower older response can never clobber a newer one that already landed.
  const fetchIdRef = useRef(0);
  // Generation bumped by every event-driven invalidation. Each fetch captures the current
  // generation and applies only if no event invalidated the resync while it was in flight.
  const epochRef = useRef(0);

  useEffect(() => {
    onStatusRef.current = onStatus;
  });

  useEffect(() => {
    keyRef.current = key;
  }, [key]);

  const invalidate = useCallback(() => {
    epochRef.current += 1;
  }, []);

  const fetchStatus = useCallback(() => {
    const fetchId = ++fetchIdRef.current;
    const epoch = epochRef.current;
    const requestedKey = keyRef.current;
    operationsApi
      .getStatus(requestedKey)
      .then((status) => {
        if (
          fetchId === fetchIdRef.current &&
          epoch === epochRef.current &&
          keyRef.current === requestedKey
        ) {
          onStatusRef.current(status);
        }
      })
      .catch(() => {
        // Leave current state as-is if the status check itself fails.
      });
  }, []);

  useEffect(() => {
    let mounted = true;
    const fetchId = ++fetchIdRef.current;
    const epoch = epochRef.current;
    const requestedKey = keyRef.current;
    operationsApi
      .getStatus(requestedKey)
      .then((status) => {
        if (
          mounted &&
          fetchId === fetchIdRef.current &&
          epoch === epochRef.current &&
          keyRef.current === requestedKey
        ) {
          onStatusRef.current(status);
        }
      })
      .catch(() => {});

    return () => {
      mounted = false;
    };
  }, [key, resyncTrigger]);

  useSignalRReconnected(fetchStatus);

  return invalidate;
}
