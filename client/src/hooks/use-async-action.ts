import { useCallback, useRef, useState } from "react";

export type ActionStatus = "idle" | "pending" | "success" | "error";

export type ActionOutcome<T> = { ok: true; value: T } | { ok: false; error: unknown };

/**
 * Tracks one async action for an <ActionButton>: idle → pending → success | error. The result
 * status sticks until the next run or `reset()`, so the button keeps showing it for as long as
 * it stays mounted. `run` never throws — it resolves to an outcome so click handlers cannot
 * leak unhandled rejections; report the outcome (e.g. with `notifications`) at the call site.
 *
 * Actions whose status comes from elsewhere (a mutation plus a polled job, say) don't need this
 * hook: derive an `ActionStatus` and pass it to <ActionButton> directly.
 */
export function useAsyncAction<Args extends unknown[], T>(action: (...args: Args) => Promise<T>) {
  const [status, setStatus] = useState<ActionStatus>("idle");
  // Only the most recent run (or reset) may write status, so a slow earlier run can't clobber it.
  const latest = useRef(0);

  const run = useCallback(
    async (...args: Args): Promise<ActionOutcome<T>> => {
      const id = ++latest.current;
      setStatus("pending");
      try {
        const value = await action(...args);
        if (id === latest.current) setStatus("success");
        return { ok: true, value };
      } catch (error) {
        if (id === latest.current) setStatus("error");
        return { ok: false, error };
      }
    },
    [action],
  );

  const reset = useCallback(() => {
    latest.current++;
    setStatus("idle");
  }, []);

  return { status, run, reset };
}
