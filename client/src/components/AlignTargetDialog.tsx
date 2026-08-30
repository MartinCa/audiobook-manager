import { useState } from "react";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";

interface AlignTargetDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  candidates: string[];
  onConfirm: (targetValue: string) => void;
}

export function AlignTargetDialog({
  open,
  onOpenChange,
  candidates,
  onConfirm,
}: AlignTargetDialogProps) {
  const [selectedTarget, setSelectedTarget] = useState<string>(candidates[0] || "");
  const [customTarget, setCustomTarget] = useState("");

  const handleConfirm = () => {
    const finalValue = selectedTarget === "custom" ? customTarget : selectedTarget;
    if (finalValue.trim()) {
      onConfirm(finalValue.trim());
      onOpenChange(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Select Target Alignment Value</DialogTitle>
        </DialogHeader>

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
              <div key={cand} className="flex items-center space-x-2">
                <RadioGroupItem value={cand} id={cand} />
                <label htmlFor={cand} className="cursor-pointer text-sm font-medium">
                  {cand}
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
            <Button variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button onClick={handleConfirm}>Confirm & Align</Button>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}

export default AlignTargetDialog;
