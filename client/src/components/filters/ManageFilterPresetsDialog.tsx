import { useState } from "react";
import { Check, Loader2, Pencil, RefreshCw, Trash2, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { AppDialog } from "@/components/AppDialog";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";
import { activeChips, type FilterFieldDef } from "@/components/filters/filterUtils";
import { readPresetFilters, sameFilters, type PresetFilters } from "@/helpers/filterPresets";
import { MAX_PRESET_NAME_LENGTH } from "./SaveFilterPresetDialog";
import type { FilterPreset } from "@/types/FilterPreset";

export interface ManageFilterPresetsDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  presets: FilterPreset[];
  /** The list's filter fields - used to describe what each preset filters by. */
  fields: FilterFieldDef[];
  /** The filters currently set on the list, which "Update" overwrites a preset with. */
  currentFilters: PresetFilters;
  /** Replaces a preset's name and filters; rejects with the error to show. */
  onUpdate: (preset: FilterPreset, name: string, filters: PresetFilters) => Promise<unknown>;
  onDelete: (preset: FilterPreset) => Promise<unknown>;
}

/**
 * Rename, overwrite with the current filters, or delete the saved presets of a list. One row is
 * busy at a time (a write refetches the list), and a delete asks for a second click on the row
 * itself rather than opening a dialog over this one.
 */
export function ManageFilterPresetsDialog({
  open,
  onOpenChange,
  presets,
  fields,
  currentFilters,
  onUpdate,
  onDelete,
}: ManageFilterPresetsDialogProps) {
  const [renamingId, setRenamingId] = useState<number | null>(null);
  const [draftName, setDraftName] = useState("");
  const [confirmingDeleteId, setConfirmingDeleteId] = useState<number | null>(null);
  const [busyId, setBusyId] = useState<number | null>(null);

  const [prevOpen, setPrevOpen] = useState(open);
  if (open !== prevOpen) {
    setPrevOpen(open);
    setRenamingId(null);
    setConfirmingDeleteId(null);
    setBusyId(null);
  }

  const hasCurrent = Object.keys(currentFilters).length > 0;

  const run = async (preset: FilterPreset, action: () => Promise<unknown>, success: string) => {
    setBusyId(preset.id);
    try {
      await action();
      notifications.success(success);
      return true;
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
      return false;
    } finally {
      setBusyId(null);
    }
  };

  const commitRename = async (preset: FilterPreset) => {
    const name = draftName.trim();
    if (name === "") return;
    if (name === preset.name) {
      setRenamingId(null);
      return;
    }
    const ok = await run(
      preset,
      () => onUpdate(preset, name, readPresetFilters(preset.filters)),
      `Renamed preset to "${name}"`,
    );
    if (ok) setRenamingId(null);
  };

  return (
    <AppDialog
      open={open}
      onOpenChange={onOpenChange}
      title="Manage filter presets"
      description="Rename a preset, replace its filters with the ones currently set, or delete it."
      footer={
        <Button variant="outline" className="w-full sm:w-auto" onClick={() => onOpenChange(false)}>
          Close
        </Button>
      }
      contentClassName="sm:max-w-2xl"
    >
      {presets.length === 0 ? (
        <p className="text-muted-foreground py-6 text-center text-sm">
          No presets saved yet. Set some filters, then choose &ldquo;Save current filters&rdquo;.
        </p>
      ) : (
        <ul className="space-y-2">
          {presets.map((preset) => {
            const filters = readPresetFilters(preset.filters);
            const summary = activeChips(fields, filters).map((chip) => chip.label);
            const busy = busyId === preset.id;
            const renaming = renamingId === preset.id;
            const confirmingDelete = confirmingDeleteId === preset.id;
            const identicalToCurrent = sameFilters(filters, currentFilters);

            return (
              <li key={preset.id} className="border-border bg-card space-y-2 rounded-lg border p-3">
                <div className="flex flex-wrap items-center gap-2">
                  {renaming ? (
                    <div className="flex min-w-0 flex-1 items-center gap-1">
                      <Input
                        autoFocus
                        value={draftName}
                        // The one place the length limit is enforced: a name cannot be typed past it.
                        maxLength={MAX_PRESET_NAME_LENGTH}
                        aria-label={`New name for preset ${preset.name}`}
                        disabled={busy}
                        onChange={(e) => setDraftName(e.target.value)}
                        onKeyDown={(e) => {
                          if (e.key === "Enter") {
                            e.preventDefault();
                            void commitRename(preset);
                          } else if (e.key === "Escape") {
                            e.stopPropagation();
                            setRenamingId(null);
                          }
                        }}
                      />
                      <Button
                        type="button"
                        size="icon-sm"
                        variant="outline"
                        aria-label="Save new name"
                        disabled={busy || draftName.trim() === ""}
                        onClick={() => {
                          void commitRename(preset);
                        }}
                      >
                        <Check />
                      </Button>
                      <Button
                        type="button"
                        size="icon-sm"
                        variant="ghost"
                        aria-label="Cancel renaming"
                        disabled={busy}
                        onClick={() => setRenamingId(null)}
                      >
                        <X />
                      </Button>
                    </div>
                  ) : (
                    <span className="text-foreground min-w-0 flex-1 font-medium break-words">
                      {preset.name}
                    </span>
                  )}

                  {busy ? <Loader2 className="text-muted-foreground h-4 w-4 animate-spin" /> : null}

                  {!renaming && !confirmingDelete ? (
                    <div className="flex items-center gap-1">
                      <Button
                        type="button"
                        size="icon-sm"
                        variant="ghost"
                        aria-label={`Rename preset ${preset.name}`}
                        disabled={busy}
                        onClick={() => {
                          setDraftName(preset.name);
                          setRenamingId(preset.id);
                        }}
                      >
                        <Pencil />
                      </Button>
                      <Button
                        type="button"
                        size="icon-sm"
                        variant="ghost"
                        aria-label={`Replace the filters of preset ${preset.name} with the current filters`}
                        title={
                          !hasCurrent
                            ? "Set some filters first"
                            : identicalToCurrent
                              ? "Already the same as the current filters"
                              : "Replace this preset's filters with the current ones"
                        }
                        disabled={busy || !hasCurrent || identicalToCurrent}
                        onClick={() => {
                          void run(
                            preset,
                            () => onUpdate(preset, preset.name, currentFilters),
                            `Updated preset "${preset.name}"`,
                          );
                        }}
                      >
                        <RefreshCw />
                      </Button>
                      <Button
                        type="button"
                        size="icon-sm"
                        variant="ghost"
                        aria-label={`Delete preset ${preset.name}`}
                        disabled={busy}
                        onClick={() => setConfirmingDeleteId(preset.id)}
                      >
                        <Trash2 />
                      </Button>
                    </div>
                  ) : null}

                  {confirmingDelete ? (
                    <div className="flex items-center gap-1">
                      <span className="text-destructive text-xs">Delete this preset?</span>
                      <Button
                        type="button"
                        size="sm"
                        variant="destructive"
                        disabled={busy}
                        onClick={() => {
                          void run(
                            preset,
                            () => onDelete(preset),
                            `Deleted preset "${preset.name}"`,
                          ).then(() => setConfirmingDeleteId(null));
                        }}
                      >
                        Delete
                      </Button>
                      <Button
                        type="button"
                        size="sm"
                        variant="outline"
                        disabled={busy}
                        onClick={() => setConfirmingDeleteId(null)}
                      >
                        Keep
                      </Button>
                    </div>
                  ) : null}
                </div>

                <p className="text-muted-foreground text-xs break-words">
                  {summary.length > 0 ? summary.join(" · ") : "No filters this list recognizes"}
                </p>
              </li>
            );
          })}
        </ul>
      )}
    </AppDialog>
  );
}

export default ManageFilterPresetsDialog;
