export interface OperationOutcomeState {
  /** The operation's status as the server reported it (`completed`, `failed`, ...). */
  status: string;
  message?: string | null;
}

/** Whether the work failed in a way the user may want to retry from the row. */
export const isFailedOutcome = (outcome: OperationOutcomeState | undefined): boolean =>
  outcome !== undefined && (outcome.status === "failed" || outcome.status === "targetExists");

/**
 * Whether a row that left the server's list should still hold its place. A finished row does, and
 * so does one still working (its outcome has not arrived). A *failed* row does not: it stays in the
 * list because it is still pending, so if it is gone it left for another reason (rejected,
 * dismissed, removed elsewhere) and showing it as a placeholder would be wrong.
 */
export const holdsPlaceWhenGone = (outcome: OperationOutcomeState | undefined): boolean =>
  !isFailedOutcome(outcome);
