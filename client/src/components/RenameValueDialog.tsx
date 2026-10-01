import { useState } from "react";
import { Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { AppDialog } from "@/components/AppDialog";
import { OperationProgressBar } from "@/components/OperationProgressBar";
import { SignalREvents } from "@/constants/signalrEvents";
import { useBookQualifiers } from "@/hooks/useBookQualifiers";
import { useSignalREvent } from "@/hooks/useSignalR";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";
import { similarValuesApi } from "@/services/api";

interface RenameProgressPayload {
  processed: number;
  total: number;
  succeeded: number;
  failed: number;
}

interface RenameCompletePayload {
  totalProcessed: number;
  totalSucceeded: number;
  totalFailed: number;
}

export interface RenameValueDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  valueType: "author" | "series";
  currentName: string;
  /** Pre-fills the new name, e.g. with the name a metadata source proposed for an author. */
  initialNewName?: string;
  /**
   * Called once every book carries the new name. The caller owns what happens next - the page's
   * route usually addresses the old name (a series) or id (an author whose books moved to a
   * freshly created entry), so it navigates and refreshes its queries.
   */
  onRenamed: (newName: string) => void | Promise<void>;
}

/**
 * Manual rename of an author or series. The backend rewrites every book through the standard
 * update pipeline (tags, folders and sidecars follow), then moves the author's / series' own
 * metadata (source match, follow, roster) to the new name - a fire-and-forget operation reporting
 * on the alignment's SignalR events, which are connection-wide, so the dialog only reacts to
 * them while it is the one that started the rename.
 *
 * The name checks mirror the backend's (the backend is the authority and refuses a bad name with
 * a 400): a comma cannot live in an author name because authors share one comma-separated tag,
 * and a series is stored without qualifier suffixes - the suffix comes from each book's own
 * qualifiers when its files are written, so the list of qualifiers comes from the backend too.
 */
export function RenameValueDialog({
  open,
  onOpenChange,
  valueType,
  currentName,
  initialNewName,
  onRenamed,
}: RenameValueDialogProps) {
  const qualifiers = useBookQualifiers();
  const [newName, setNewName] = useState(initialNewName ?? currentName);
  const [renaming, setRenaming] = useState(false);
  const [progress, setProgress] = useState<RenameProgressPayload | null>(null);

  // Reset whenever the dialog opens (or is pointed at another value while open): the instance is
  // reused across authors/series because the pages it lives on do not remount on a param change.
  const [prevOpen, setPrevOpen] = useState(open);
  const [prevCurrent, setPrevCurrent] = useState(currentName);
  if (open !== prevOpen || currentName !== prevCurrent) {
    setPrevOpen(open);
    setPrevCurrent(currentName);
    if (open) {
      setNewName(initialNewName ?? currentName);
      setRenaming(false);
      setProgress(null);
    }
  }

  const trimmed = newName.trim();
  const noun = valueType === "author" ? "author" : "series";

  let problem: string | null = null;
  if (trimmed !== "" && trimmed === currentName.trim()) {
    problem = `That is already this ${noun}'s name.`;
  } else if (valueType === "author" && trimmed.includes(",")) {
    problem =
      "An author name cannot contain a comma: authors are stored comma-separated in the file's tags, so it would be read back as two authors.";
  } else if (valueType === "series") {
    const qualifier = qualifiers.find(
      (q) =>
        trimmed.toLowerCase().endsWith(q.suffix.trim().toLowerCase()) &&
        trimmed.length > q.suffix.trim().length,
    );
    if (qualifier) {
      problem = `Leave off "(${qualifier.label})": series names are stored without qualifiers, and each book's own qualifiers are added to its files.`;
    }
  }

  const canSubmit = trimmed !== "" && problem === null && !renaming;

  useSignalREvent<RenameProgressPayload>(SignalREvents.SimilarValueAlignProgress, (data) => {
    if (renaming) setProgress(data);
  });

  useSignalREvent<RenameCompletePayload>(SignalREvents.SimilarValueAlignComplete, (data) => {
    if (!renaming) return;
    setRenaming(false);
    setProgress(null);
    if (data.totalFailed > 0) {
      // The old name is still on the books that failed (and the author/series entry itself stays
      // there too), so running the same rename again finishes the job.
      notifications.warning(
        `Renamed ${data.totalSucceeded} of ${data.totalProcessed} books; ${data.totalFailed} failed. Run the rename again to finish.`,
      );
      return;
    }

    notifications.success(`Renamed ${currentName} to ${trimmed}`);
    void Promise.resolve(onRenamed(trimmed)).catch((err: unknown) => {
      notifications.error(handleApiError(err).message);
    });
    onOpenChange(false);
  });

  const handleSubmit = async () => {
    if (!canSubmit) return;
    setRenaming(true);
    setProgress(null);
    try {
      await similarValuesApi.rename(valueType, currentName.trim(), trimmed);
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
      setRenaming(false);
    }
  };

  return (
    <AppDialog
      open={open}
      // A running rename is rewriting files; closing the dialog would hide the only place its
      // result is reported.
      onOpenChange={(next) => {
        if (!renaming) onOpenChange(next);
      }}
      title={`Rename ${noun}`}
      description={
        valueType === "author"
          ? "Renames the author on every one of their books - tags, folders and sidecar files are rewritten. If an author with the new name already exists the two are merged."
          : "Renames the series on every book that lists it - tags, folders and sidecar files are rewritten. Each book keeps its own qualifiers (such as Dramatized)."
      }
      footer={
        <>
          <Button
            variant="outline"
            className="w-full sm:w-auto"
            disabled={renaming}
            onClick={() => onOpenChange(false)}
          >
            Cancel
          </Button>
          <Button
            className="w-full sm:w-auto"
            disabled={!canSubmit}
            onClick={() => {
              void handleSubmit();
            }}
          >
            {renaming ? <Loader2 className="mr-1.5 h-4 w-4 animate-spin" /> : null}
            {renaming ? "Renaming" : "Rename"}
          </Button>
        </>
      }
    >
      <div className="space-y-3 text-sm">
        <div className="space-y-1">
          <div className="text-muted-foreground text-xs">Current name</div>
          <div className="text-foreground font-medium break-words">{currentName}</div>
        </div>

        <div className="space-y-1">
          <label htmlFor="rename-value-input" className="text-muted-foreground text-xs">
            New name
          </label>
          <Input
            id="rename-value-input"
            value={newName}
            disabled={renaming}
            onChange={(e) => setNewName(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") {
                e.preventDefault();
                void handleSubmit();
              }
            }}
            aria-invalid={problem !== null}
          />
          {problem ? <p className="text-destructive text-xs break-words">{problem}</p> : null}
        </div>

        {renaming ? (
          <OperationProgressBar
            processed={progress?.processed ?? 0}
            total={progress?.total ?? 0}
            label={`Renaming ${noun}...`}
            subText={
              progress ? `${progress.succeeded} updated, ${progress.failed} failed` : undefined
            }
          />
        ) : null}
      </div>
    </AppDialog>
  );
}

export default RenameValueDialog;
