import { useState } from "react";
import { Bookmark, ChevronDown } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";
import { useFilterPresets } from "@/hooks/useFilterPresets";
import {
  activeFilterValues,
  applyPresetTo,
  readPresetFilters,
  sameFilters,
} from "@/helpers/filterPresets";
import type { FilterFieldDef, FilterValueMap } from "./filterUtils";
import { ManageFilterPresetsDialog } from "./ManageFilterPresetsDialog";
import { SaveFilterPresetDialog } from "./SaveFilterPresetDialog";
import type { FilterPresetScope } from "@/types/FilterPreset";

interface FilterPresetsProps {
  /** Which list's presets: they are kept per list, so a book preset never shows on authors. */
  scope: FilterPresetScope;
  /** The filters currently set on the list (its route search params). */
  filters: FilterValueMap;
  /** The list's filter fields, used to describe a preset's contents in the manage dialog. */
  fields: FilterFieldDef[];
  /** Replaces the list's filters - the same callback the list hands its filter bar. */
  onApply: (next: FilterValueMap) => void;
}

/**
 * Named, saved filter sets for a library list: apply one from the menu, save the current filters
 * under a name, or manage (rename / overwrite / delete) the saved ones. Presets live on the server
 * so they follow the person across browsers, and they hold filters only - the search text is not
 * part of one. Applying a preset replaces the list's filters rather than adding to them, and the
 * menu shows which preset the current filters are (if any), so a hand-tweaked set reads as "not a
 * preset" instead of keeping a stale name.
 */
export function FilterPresets({ scope, filters, fields, onApply }: FilterPresetsProps) {
  const { presets, isLoading, error, refetch, create, update, remove } = useFilterPresets(scope);
  const [saveOpen, setSaveOpen] = useState(false);
  const [manageOpen, setManageOpen] = useState(false);

  const current = activeFilterValues(filters);
  const hasCurrent = Object.keys(current).length > 0;
  const activePreset = presets.find((p) => sameFilters(readPresetFilters(p.filters), current));

  const apply = (id: string) => {
    const preset = presets.find((p) => String(p.id) === id);
    if (preset) onApply(applyPresetTo(filters, readPresetFilters(preset.filters)));
  };

  if (error) {
    return (
      <div className="text-muted-foreground flex flex-wrap items-center gap-2 text-xs">
        <span>Filter presets could not be loaded: {handleApiError(error).message}</span>
        <Button type="button" variant="outline" size="sm" onClick={() => void refetch()}>
          Retry
        </Button>
      </div>
    );
  }

  return (
    <div className="flex flex-wrap items-center gap-2">
      <span className="text-muted-foreground text-xs font-semibold uppercase">Presets</span>
      <DropdownMenu>
        <DropdownMenuTrigger
          render={
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={isLoading}
              className="h-8 max-w-64 justify-between font-normal"
            >
              <Bookmark className="h-3.5 w-3.5 shrink-0" />
              <span className="truncate">
                {isLoading ? "Loading presets..." : (activePreset?.name ?? "Apply a preset")}
              </span>
              <ChevronDown className="h-3.5 w-3.5 shrink-0 opacity-50" />
            </Button>
          }
        />
        <DropdownMenuContent align="start" className="min-w-56">
          {presets.length === 0 ? (
            <DropdownMenuItem disabled>No presets saved yet</DropdownMenuItem>
          ) : (
            <DropdownMenuRadioGroup
              value={activePreset ? String(activePreset.id) : ""}
              onValueChange={apply}
            >
              {presets.map((preset) => (
                <DropdownMenuRadioItem
                  key={preset.id}
                  value={String(preset.id)}
                  // A radio item keeps the menu open by default; choosing a preset is a one-shot
                  // action whose result is on the page behind the menu.
                  closeOnClick
                >
                  <span className="truncate">{preset.name}</span>
                </DropdownMenuRadioItem>
              ))}
            </DropdownMenuRadioGroup>
          )}
          <DropdownMenuSeparator />
          <DropdownMenuItem disabled={!hasCurrent} onClick={() => setSaveOpen(true)}>
            Save current filters...
          </DropdownMenuItem>
          <DropdownMenuItem disabled={presets.length === 0} onClick={() => setManageOpen(true)}>
            Manage presets...
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>

      <SaveFilterPresetDialog
        open={saveOpen}
        onOpenChange={setSaveOpen}
        filterCount={Object.keys(current).length}
        onSave={async (name) => {
          await create.mutateAsync({ name, filters: current });
          notifications.success(`Saved preset "${name}"`);
        }}
      />

      <ManageFilterPresetsDialog
        open={manageOpen}
        onOpenChange={setManageOpen}
        presets={presets}
        fields={fields}
        currentFilters={current}
        onUpdate={(preset, name, nextFilters) =>
          update.mutateAsync({ id: preset.id, name, filters: nextFilters })
        }
        onDelete={(preset) => remove.mutateAsync(preset.id)}
      />
    </div>
  );
}

export default FilterPresets;
