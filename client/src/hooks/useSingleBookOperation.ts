import { useCallback, useEffect, useRef, useState } from "react";
import { SignalREvents, type SingleBookOperation } from "@/constants/signalrEvents";
import { useSignalR, useSignalREvent, useSignalRReconnected } from "@/hooks/useSignalR";

/** SingleBookOperationComplete server-side (Api/Async/SingleBookOperationEvents.cs). */
export interface SingleBookOperationCompletePayload {
  audiobookId: number;
  operation: string;
  /** `completed`, `nothingToApply`, `targetExists` or `failed`. */
  status: string;
  /** Safe to show; null/absent for a plain success. */
  message?: string | null;
}

export interface SingleBookOperationHandle {
  /** The books this page has started the operation for and not yet heard back about. */
  busyIds: ReadonlySet<number>;
  /**
   * Sends the request that starts the operation for a book, tracking the book as busy for as long
   * as it is outstanding. Resolves once the server has accepted it (202); rejects, and un-marks the
   * book, when the server refuses it. The book is marked busy *before* the request goes out: the
   * background task starts before the 202 is returned, so a fast completion can reach the page
   * first, and marking it afterwards would resurrect a busy state nothing would ever clear.
   */
  start: (audiobookId: number, request: () => Promise<void>) => Promise<void>;
}

/**
 * Follows a single-book operation the server runs in the background (select a pending online
 * match, apply one pending refresh). The server answers 202 as soon as the request is accepted and
 * reports the outcome as a SingleBookOperationComplete event, so the row that started the work can
 * collapse straight away and the user can start the next one; that is also why the listener lives
 * here, on the page, and not in the row panel that unmounts when it collapses.
 *
 * An event sent while the hub is not connected is gone for good, so whenever the connection comes
 * (back) up - a reconnect, or the first successful start after a failed initial one, which the
 * provider does not report as a reconnect - the busy set is cleared and `onResync` runs. A row
 * stuck in "working" forever is worse than a row that reappears (the server answers 409 if its
 * work is in fact still running).
 */
export function useSingleBookOperation(
  operation: SingleBookOperation,
  onComplete: (event: SingleBookOperationCompletePayload) => void,
  onResync: () => void,
): SingleBookOperationHandle {
  const [busyIds, setBusyIds] = useState<ReadonlySet<number>>(() => new Set());
  const { isConnected } = useSignalR();

  const onResyncRef = useRef(onResync);
  useEffect(() => {
    onResyncRef.current = onResync;
  });

  const removeBusy = useCallback((audiobookId: number) => {
    setBusyIds((prev) => {
      if (!prev.has(audiobookId)) return prev;
      const next = new Set(prev);
      next.delete(audiobookId);
      return next;
    });
  }, []);

  const start = useCallback(
    async (audiobookId: number, request: () => Promise<void>) => {
      setBusyIds((prev) => new Set(prev).add(audiobookId));
      try {
        await request();
      } catch (err) {
        removeBusy(audiobookId);
        throw err;
      }
    },
    [removeBusy],
  );

  useSignalREvent<SingleBookOperationCompletePayload>(
    SignalREvents.SingleBookOperationComplete,
    (event) => {
      if (event.operation !== operation) return;
      removeBusy(event.audiobookId);
      onComplete(event);
    },
  );

  const resync = useCallback(() => {
    setBusyIds(new Set());
    onResyncRef.current();
  }, []);

  useSignalRReconnected(resync);

  // Connecting for the first time (or again after the connection closed) is not a "reconnected"
  // callback, but anything started while the hub was down has lost its event just the same.
  const wasConnected = useRef(isConnected);
  useEffect(() => {
    if (isConnected && !wasConnected.current) resync();
    wasConnected.current = isConnected;
  }, [isConnected, resync]);

  return { busyIds, start };
}
