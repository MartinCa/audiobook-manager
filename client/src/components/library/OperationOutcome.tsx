import { AlertCircle, CheckCircle2, Loader2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";

export interface OperationOutcomeState {
  /** The operation's status as the server reported it (`completed`, `failed`, ...). */
  status: string;
  message?: string | null;
}

interface OperationOutcomeProps {
  /** The work was accepted and has not reported back yet. */
  busy: boolean;
  /** Shown while busy, e.g. "Applying…". */
  busyLabel: string;
  outcome: OperationOutcomeState | undefined;
  /** What each reported status is called on this page; an unlisted non-failure status shows nothing. */
  labels: Record<string, string>;
}

/** Whether the outcome is one the user may want to retry from the row. */
const isFailedOutcome = (outcome: OperationOutcomeState | undefined): boolean =>
  outcome !== undefined && (outcome.status === "failed" || outcome.status === "targetExists");

/**
 * The badge on a row that has work in flight or has finished it. A finished row stays put (see
 * useKeptRows), so this is what tells the user how it ended.
 */
export function OperationOutcomeBadge({ busy, busyLabel, outcome, labels }: OperationOutcomeProps) {
  if (busy) {
    return (
      <Badge variant="outline" className="shrink-0">
        <Loader2 className="mr-1 h-3 w-3 animate-spin" />
        {busyLabel}
      </Badge>
    );
  }
  if (!outcome) return null;
  const label = labels[outcome.status];
  if (isFailedOutcome(outcome)) {
    return (
      <Badge variant="destructive" className="shrink-0">
        <AlertCircle className="mr-1 h-3 w-3" />
        {label ?? "Failed"}
      </Badge>
    );
  }
  if (!label) return null;
  return (
    <Badge variant="secondary" className="shrink-0">
      <CheckCircle2 className="mr-1 h-3 w-3" />
      {label}
    </Badge>
  );
}

/** The reason a row's work failed, under its title. */
export function OperationOutcomeMessage({
  outcome,
}: {
  outcome: OperationOutcomeState | undefined;
}) {
  if (!isFailedOutcome(outcome) || !outcome?.message) return null;
  return <div className="text-destructive mt-0.5 text-xs">{outcome.message}</div>;
}
