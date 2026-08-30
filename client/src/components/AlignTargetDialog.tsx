import { useState } from "react";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import type { SimilarValueCandidate } from "@/types/SimilarValue";

interface AlignTargetDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  candidates: SimilarValueCandidate[];
  valueType: "author" | "series";
  onConfirm: (targetValue: string) => void;
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

  const resetAndClose = () => {
    setStep("select");
    onOpenChange(false);
  };

  const finalValue = (selectedTarget === "custom" ? customTarget : selectedTarget).trim();

  const handleContinue = () => {
    if (finalValue) setStep("confirm");
  };

  const handleConfirm = () => {
    onConfirm(finalValue);
    resetAndClose();
  };

  // Books that already carry the chosen target are not touched: AlignAuthorsAsync /
  // AlignSeriesAsync drop the target from the source list before querying. A free-text target
  // matches no candidate, so there every candidate's books really are affected.
  const affectedBookCount = candidates
    .filter((c) => c.value !== finalValue)
    .reduce((sum, c) => sum + c.bookCount, 0);

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) resetAndClose();
        else onOpenChange(next);
      }}
    >
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>
            {step === "select" ? "Select Target Alignment Value" : "Confirm Alignment"}
          </DialogTitle>
        </DialogHeader>

        {step === "select" ? (
          <div className="space-y-4 py-2">
            <p className="text-muted-foreground text-xs">
              Choose which canonical value to apply across all matched entries:
            </p>

            <RadioGroup
              value={selectedTarget}
              onValueChange={setSelectedTarget}
              className="space-y-2"
            >
              {candidates.map((cand) => (
                <div key={cand.value} className="flex items-center space-x-2">
                  <RadioGroupItem value={cand.value} id={cand.value} />
                  <label htmlFor={cand.value} className="cursor-pointer text-sm font-medium">
                    {cand.value}
                  </label>
                </div>
              ))}
              <div className="flex items-center space-x-2">
                <RadioGroupItem value="custom" id="custom" />
                <label htmlFor="custom" className="cursor-pointer text-sm font-medium">
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

            <div className="border-border flex justify-end gap-2 border-t pt-4">
              <Button variant="outline" onClick={resetAndClose}>
                Cancel
              </Button>
              <Button onClick={handleContinue} disabled={!finalValue}>
                Continue
              </Button>
            </div>
          </div>
        ) : (
          <div className="space-y-4 py-2">
            <p className="text-muted-foreground text-xs">
              This will update{" "}
              <strong>
                {affectedBookCount} book{affectedBookCount === 1 ? "" : "s"}
              </strong>{" "}
              to use <strong>&quot;{finalValue}&quot;</strong> as the {valueType}. Each affected
              book&apos;s m4b tags will be rewritten and the file relocated if needed. This action
              cannot be undone.
            </p>
            <div className="border-border flex justify-end gap-2 border-t pt-4">
              <Button variant="outline" onClick={() => setStep("select")}>
                Back
              </Button>
              <Button onClick={handleConfirm}>Apply</Button>
            </div>
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}

export default AlignTargetDialog;
