import { AlertCircle, CheckCircle2, Sparkles, PlusCircle } from "lucide-react";
import type { EntryStatus } from "@/types/EntryStatus";

interface EntryStatusHintProps {
  /**
   * The classification to show. Null means "not yet known" (loading, or the value is blank) and
   * renders nothing, so the field stays quiet until there is something to say.
   */
  status: EntryStatus | null;
  /**
   * Whether the bounded classification query failed. Rendered as an explicit error note in place
   * of a silent "new" guess - an indicator that could not reach the server must not invent a
   * classification.
   */
  isError?: boolean;
  /**
   * Called when the user clicks a "similar" suggestion. When absent, similar matches render as
   * informational text instead of an actionable hint.
   */
  onUseMatch?: (name: string) => void;
}

/**
 * The explicit exact-existing / similar / new indicator for one author or series entry. Backed by
 * the bounded server-side classification (see useEntryStatus) - deliberately not the client-side
 * scan over the unbounded name lists it replaces.
 */
export function EntryStatusHint({ status, isError = false, onUseMatch }: EntryStatusHintProps) {
  if (!status || !status.value.trim()) {
    if (isError) {
      return (
        <p className="text-status-error mt-1 flex items-center gap-1 text-xs" role="alert">
          <AlertCircle className="h-3 w-3 shrink-0" />
          <span>Couldn't check the library for existing entries.</span>
        </p>
      );
    }
    return null;
  }

  if (status.status === "exact" && status.exactMatch) {
    return (
      <p className="text-status-ok mt-1 flex items-center gap-1 text-xs">
        <CheckCircle2 className="h-3 w-3 shrink-0" />
        <span className="break-words">{status.exactMatch.name}</span>
        <span className="text-status-unknown">— existing entry</span>
      </p>
    );
  }

  if (status.status === "similar" && status.similarMatches.length > 0) {
    const first = status.similarMatches[0]!;
    const hint =
      status.similarMatches.length === 1
        ? `Similar to "${first.name}"`
        : `Similar to "${first.name}" (${status.similarMatches.length} candidates)`;
    if (onUseMatch) {
      return (
        <button
          type="button"
          className="text-status-warn hover:text-foreground mt-1 block cursor-pointer text-xs underline decoration-dotted"
          onClick={() => onUseMatch(first.name)}
        >
          <Sparkles className="mr-1 inline h-3 w-3" />
          {hint} — click to use
        </button>
      );
    }
    return (
      <p className="text-status-warn mt-1 flex items-center gap-1 text-xs">
        <Sparkles className="h-3 w-3 shrink-0" />
        <span className="break-words">{hint}</span>
      </p>
    );
  }

  if (status.status === "new") {
    return (
      <p className="text-status-unknown mt-1 flex items-center gap-1 text-xs">
        <PlusCircle className="h-3 w-3 shrink-0" />
        <span>New — no exact or similar entry in the library</span>
      </p>
    );
  }

  return null;
}
