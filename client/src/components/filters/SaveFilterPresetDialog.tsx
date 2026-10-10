import { useState } from "react";
import { Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { AppDialog } from "@/components/AppDialog";
import { handleApiError } from "@/lib/api";

/** Mirrors the backend's FilterPresetRules.MaxNameLength; the backend stays the authority. */
export const MAX_PRESET_NAME_LENGTH = 80;

export interface SaveFilterPresetDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Saves the preset; a rejection (a taken name, a full list) is shown under the name field. */
  onSave: (name: string) => Promise<unknown>;
  /** How many filters will be saved, for the description. */
  filterCount: number;
}

/** Names the current filters and saves them as a preset. */
export function SaveFilterPresetDialog({
  open,
  onOpenChange,
  onSave,
  filterCount,
}: SaveFilterPresetDialogProps) {
  const [name, setName] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Start every opening from a blank form: the instance outlives a single save.
  const [prevOpen, setPrevOpen] = useState(open);
  if (open !== prevOpen) {
    setPrevOpen(open);
    if (open) {
      setName("");
      setSaving(false);
      setError(null);
    }
  }

  const trimmed = name.trim();
  const canSave = trimmed !== "" && !saving;

  const handleSave = async () => {
    if (!canSave) return;
    setSaving(true);
    setError(null);
    try {
      await onSave(trimmed);
      onOpenChange(false);
    } catch (err: unknown) {
      setError(handleApiError(err).message);
    } finally {
      setSaving(false);
    }
  };

  return (
    <AppDialog
      open={open}
      onOpenChange={(next) => {
        if (!saving) onOpenChange(next);
      }}
      title="Save filter preset"
      description={`Saves the ${filterCount} active ${filterCount === 1 ? "filter" : "filters"} under a name so you can apply them again later. The search text is not saved.`}
      footer={
        <>
          <Button
            variant="outline"
            className="w-full sm:w-auto"
            disabled={saving}
            onClick={() => onOpenChange(false)}
          >
            Cancel
          </Button>
          <Button
            className="w-full sm:w-auto"
            disabled={!canSave}
            onClick={() => {
              void handleSave();
            }}
          >
            {saving ? <Loader2 className="mr-1.5 h-4 w-4 animate-spin" /> : null}
            {saving ? "Saving" : "Save preset"}
          </Button>
        </>
      }
    >
      <div className="space-y-1 text-sm">
        <label htmlFor="filter-preset-name" className="text-muted-foreground text-xs">
          Preset name
        </label>
        <Input
          id="filter-preset-name"
          value={name}
          disabled={saving}
          maxLength={MAX_PRESET_NAME_LENGTH}
          placeholder="e.g. Unsupported backlog"
          onChange={(e) => {
            setName(e.target.value);
            setError(null);
          }}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              e.preventDefault();
              void handleSave();
            }
          }}
          aria-invalid={error !== null}
        />
        {error ? <p className="text-destructive text-xs break-words">{error}</p> : null}
      </div>
    </AppDialog>
  );
}

export default SaveFilterPresetDialog;
