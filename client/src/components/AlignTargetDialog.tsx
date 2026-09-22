import { useState } from "react";
import { AppDialog } from "@/components/AppDialog";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import type { SimilarValueCandidate } from "@/types/SimilarValue";

interface AlignTargetDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  candidates: SimilarValueCandidate[];
  valueType: "author" | "series";
  /** includedValues is the checked candidates only - the values this alignment should touch. */
  onConfirm: (targetValue: string, includedValues: string[]) => void;
}

export function AlignTargetDialog({
  open,
  onOpenChange,
  candidates,
  valueType,
  onConfirm,
}: AlignTargetDialogProps) {
  const [step, setStep] = useState<"select" | "confirm">("select");
  const [selectedTarget, setSelectedTarget] = useState<string>(candidates[0]?.value || "");
  const [customTarget, setCustomTarget] = useState("");
  // All candidates start checked (included); unchecking one excludes it from this alignment
  // without removing it from the detected group.
  const [checkedValues, setCheckedValues] = useState<Set<string>>(
    () => new Set(candidates.map((c) => c.value)),
  );

  const resetAndClose = () => {
    setStep("select");
    setSelectedTarget(candidates[0]?.value || "");
    setCustomTarget("");
    setCheckedValues(new Set(candidates.map((c) => c.value)));
    onOpenChange(false);
  };

  const finalValue = (selectedTarget === "custom" ? customTarget : selectedTarget).trim();

  // The custom-value option has no checkbox: a free-text target is definitionally what the
  // checked candidates align to, so it always counts as included.
  const includedCandidates = candidates.filter((c) => checkedValues.has(c.value));
  const includedValues = includedCandidates.map((c) => c.value);

  // Continue is only meaningful once there is a valid target AND at least one checked candidate
  // would actually change - a target value already matched by every checked candidate does
  // nothing.
  const wouldChangeSomething =
    finalValue.length > 0 && includedValues.some((v) => v !== finalValue);

  const handleToggleCandidate = (value: string, checked: boolean) => {
    setCheckedValues((prev) => {
      const next = new Set(prev);
      if (checked) next.add(value);
      else next.delete(value);
      return next;
    });

    // A candidate that gets unchecked cannot stay the selected radio target - fall back to
    // another checked candidate, or to "custom" if none remain checked.
    if (!checked && selectedTarget === value) {
      const fallback = candidates.find((c) => c.value !== value && checkedValues.has(c.value));
      setSelectedTarget(fallback?.value ?? "custom");
    }
  };

  const handleContinue = () => {
    if (wouldChangeSomething) setStep("confirm");
  };

  const handleConfirm = () => {
    onConfirm(finalValue, includedValues);
    resetAndClose();
  };

  // Books that already carry the chosen target are not touched: AlignAuthorsAsync /
  // AlignSeriesAsync drop the target from the source list before querying. A free-text target
  // matches no candidate, so there every checked candidate's books really are affected.
  const affectedBookCount = includedCandidates
    .filter((c) => c.value !== finalValue)
    .reduce((sum, c) => sum + c.bookCount, 0);

  const someUnchecked = checkedValues.size < candidates.length;

  return (
    <AppDialog
      open={open}
      onOpenChange={(next) => {
        if (!next) resetAndClose();
        else onOpenChange(next);
      }}
      title={step === "select" ? "Select Target Alignment Value" : "Confirm Alignment"}
      contentClassName="sm:max-w-md"
      footer={
        step === "select" ? (
          <>
            <Button variant="outline" className="w-full sm:w-auto" onClick={resetAndClose}>
              Cancel
            </Button>
            <Button
              className="w-full sm:w-auto"
              onClick={handleContinue}
              disabled={!wouldChangeSomething}
            >
              Continue
            </Button>
          </>
        ) : (
          <>
            <Button
              variant="outline"
              className="w-full sm:w-auto"
              onClick={() => setStep("select")}
            >
              Back
            </Button>
            <Button className="w-full sm:w-auto" onClick={handleConfirm}>
              Apply
            </Button>
          </>
        )
      }
    >
      {step === "select" ? (
        <div className="space-y-4">
          <p className="text-muted-foreground text-xs">
            {someUnchecked
              ? "Choose which values to merge and the canonical value to apply:"
              : "Choose which canonical value to apply across all matched entries:"}
          </p>

          <RadioGroup
            value={selectedTarget}
            onValueChange={setSelectedTarget}
            className="space-y-2"
          >
            {candidates.map((cand) => {
              const checked = checkedValues.has(cand.value);
              return (
                <div key={cand.value} className="flex items-center space-x-2">
                  <Checkbox
                    checked={checked}
                    onCheckedChange={(next) => handleToggleCandidate(cand.value, next === true)}
                    aria-label={`Include "${cand.value}" in this alignment`}
                  />
                  <RadioGroupItem
                    value={cand.value}
                    id={cand.value}
                    disabled={!checked}
                    className="shrink-0"
                  />
                  <label
                    htmlFor={cand.value}
                    className={`min-w-0 text-sm font-medium break-words ${checked ? "cursor-pointer" : "text-muted-foreground cursor-not-allowed"}`}
                  >
                    {cand.value}
                  </label>
                </div>
              );
            })}
            <div className="flex items-center space-x-2">
              <RadioGroupItem value="custom" id="custom" className="ml-6 shrink-0" />
              <label htmlFor="custom" className="shrink-0 cursor-pointer text-sm font-medium">
                Custom value:
              </label>
            </div>
          </RadioGroup>

          {selectedTarget === "custom" && (
            <Input
              placeholder="Enter custom value..."
              value={customTarget}
              onChange={(e) => setCustomTarget(e.target.value)}
            />
          )}
        </div>
      ) : (
        <p className="text-muted-foreground text-xs">
          This will update{" "}
          <strong>
            {affectedBookCount} book{affectedBookCount === 1 ? "" : "s"}
          </strong>{" "}
          to use <strong>&quot;{finalValue}&quot;</strong> as the {valueType}
          {someUnchecked ? " (only the checked values above are included)" : ""}. Each affected
          book&apos;s m4b tags will be rewritten and the file relocated if needed. This action
          cannot be undone.
        </p>
      )}
    </AppDialog>
  );
}

export default AlignTargetDialog;
