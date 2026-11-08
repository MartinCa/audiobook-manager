import { useCallback, useState } from "react";
import { SignalREvents, type SingleBookOperation } from "@/constants/signalrEvents";
import { useSignalREvent, useSignalRReconnected } from "@/hooks/useSignalR";

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
  /** Call once the server has accepted the request (202); the row can collapse from here. */
  markStarted: (audiobookId: number) => void;
}

/**
 * Follows a single-book operation the server runs in the background (select a pending online
 * match, apply one pending refresh). The server answers 202 as soon as the request is accepted and
 * reports the outcome as a SingleBookOperationComplete event, so the row that started the work can
 * collapse straight away and the user can start the next one; that is also why the listener lives
 * here, on the page, and not in the row panel that unmounts when it collapses.
 *
 * After a SignalR reconnect the busy set is cleared and `onResync` runs: an event sent while the
 * connection was down is gone for good, and a row stuck in "working" forever is worse than a row
 * that reappears (the server answers 409 if its work is in fact still running).
 */
export function useSingleBookOperation(
  operation: SingleBookOperation,
  onComplete: (event: SingleBookOperationCompletePayload) => void,
  onResync: () => void,
): SingleBookOperationHandle {
  const [busyIds, setBusyIds] = useState<ReadonlySet<number>>(() => new Set());

  const markStarted = useCallback((audiobookId: number) => {
    setBusyIds((prev) => new Set(prev).add(audiobookId));
  }, []);

  useSignalREvent<SingleBookOperationCompletePayload>(
    SignalREvents.SingleBookOperationComplete,
    (event) => {
      if (event.operation !== operation) return;
      setBusyIds((prev) => {
        if (!prev.has(event.audiobookId)) return prev;
        const next = new Set(prev);
        next.delete(event.audiobookId);
        return next;
      });
      onComplete(event);
    },
  );

  useSignalRReconnected(() => {
    setBusyIds(new Set());
    onResync();
  });

  return { busyIds, markStarted };
}
