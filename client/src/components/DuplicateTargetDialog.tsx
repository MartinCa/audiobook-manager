import React, { useState } from "react";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";

interface DuplicateTargetDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  candidates: string[];
  onConfirm: (targetValue: string) => void;
}

export const DuplicateTargetDialog: React.FC<DuplicateTargetDialogProps> = ({
  open,
  onOpenChange,
  candidates,
  onConfirm,
}) => {
  const [selectedTarget, setSelectedTarget] = useState<string>(
    candidates[0] || "",
  );
  const [customTarget, setCustomTarget] = useState("");

  const handleConfirm = () => {
    const finalValue =
      selectedTarget === "custom" ? customTarget : selectedTarget;
    if (finalValue.trim()) {
      onConfirm(finalValue.trim());
      onOpenChange(false);
    }
  };

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
    >
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Select Target Alignment Value</DialogTitle>
        </DialogHeader>

        <div className="space-y-4 py-2">
          <p className="text-xs text-muted-foreground">
            Choose which canonical value to apply across all matched entries:
          </p>

          <RadioGroup
            value={selectedTarget}
            onValueChange={setSelectedTarget}
            className="space-y-2"
          >
            {candidates.map((cand) => (
              <div
                key={cand}
                className="flex items-center space-x-2"
              >
                <RadioGroupItem
                  value={cand}
                  id={cand}
                />
                <label
                  htmlFor={cand}
                  className="text-sm cursor-pointer font-medium"
                >
                  {cand}
                </label>
              </div>
            ))}
            <div className="flex items-center space-x-2">
              <RadioGroupItem
                value="custom"
                id="custom"
              />
              <label
                htmlFor="custom"
                className="text-sm cursor-pointer font-medium"
              >
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

          <div className="flex justify-end gap-2 pt-4 border-t border-border">
            <Button
              variant="outline"
              onClick={() => onOpenChange(false)}
            >
              Cancel
            </Button>
            <Button onClick={handleConfirm}>Confirm & Align</Button>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
};
export default DuplicateTargetDialog;
