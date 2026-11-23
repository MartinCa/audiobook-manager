import { useState } from "react";

import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import {
  PrimarySeriesChooser,
  type PrimarySeriesChooserProps,
} from "@/components/PrimarySeriesChooser";
import { MainAuthorChooser, type MainAuthorChooserProps } from "@/components/MainAuthorChooser";
import type { FieldDiff } from "@/hooks/useMetadataFieldDiffs";

interface MetadataFieldDiffTableProps {
  fields: FieldDiff[];
  selected: Set<string>;
  onToggleField: (key: string) => void;
  onToggleAll: () => void;
  changedFieldKeys: string[];
  /**
   * When the source reports several series, the Series row's "New Value" cell becomes the
   * primary-series choice (a radio per series) instead of plain text. Omit for a single series.
   */
  seriesChoice?: PrimarySeriesChooserProps;
  /**
   * When the source reports several authors, the Authors row's "New Value" cell gains the
   * main-author choice under the names. Omit for a single author.
   */
  authorsChoice?: MainAuthorChooserProps;
  /**
   * Changes once the inputs the diff is derived from have finished loading (the language list,
   * the qualifier options). The row order is frozen between changes of this key, so the order is
   * re-derived once when some `changed` flags settle, not on every toggle.
   */
  orderKey?: string;
}

/** Row position per field key with changed rows first and each group in its original order. */
function rankChangedFirst(fields: FieldDiff[]): Map<string, number> {
  const sorted = [...fields].sort((a, b) => Number(b.changed) - Number(a.changed));
  return new Map(sorted.map((f, i) => [f.key, i]));
}

/**
 * The selectable field-diff grid shared by TagPreviewDialog (every field, in a modal) and
 * PendingRefreshRowPanel (changed fields only, inline in the metadata-refresh list) - kept as one
 * component so the two apply flows never drift on how a diff row looks or behaves.
 */
export function MetadataFieldDiffTable({
  fields,
  selected,
  onToggleField,
  onToggleAll,
  changedFieldKeys,
  seriesChoice,
  authorsChoice,
  orderKey = "",
}: MetadataFieldDiffTableProps) {
  // Changed rows first (each group keeps its field order): the preview lists every field, and on
  // a phone the dialog body only fits a handful of rows, so a late-ordered changed field
  // (Qualifiers, URL) used to sit below the fold while the counter said it was selected. The order
  // is frozen per mount (re-derived only when the set of rows or `orderKey` changes): `changed` is live state
  // the user flips while interacting (the split-title toggle, the main-author radios), and rows
  // must not jump under the pointer while a diff is being resolved.
  const rowKeys = `${orderKey}#${fields.map((f) => f.key).join("|")}`;
  const [frozen, setFrozen] = useState(() => ({ rowKeys, rank: rankChangedFirst(fields) }));
  let rank = frozen.rank;
  if (frozen.rowKeys !== rowKeys) {
    rank = rankChangedFirst(fields);
    setFrozen({ rowKeys, rank });
  }
  const orderedFields = [...fields].sort(
    (a, b) =>
      (rank.get(a.key) ?? Number.MAX_SAFE_INTEGER) - (rank.get(b.key) ?? Number.MAX_SAFE_INTEGER),
  );

  return (
    <div className="space-y-2">
      <div className="flex flex-col justify-between gap-2 sm:flex-row sm:items-center">
        <Button
          variant="ghost"
          className="h-7 w-full justify-start text-xs sm:w-auto sm:justify-center"
          onClick={onToggleAll}
        >
          {selected.size === changedFieldKeys.length
            ? "Deselect All Changed"
            : "Select All Changed"}
        </Button>
        <span className="text-muted-foreground text-[11px] sm:text-xs">
          {selected.size} of {changedFieldKeys.length} changed fields selected
        </span>
      </div>

      {/* No inner vertical scroll region: the table grows to its content and the surrounding
          scroller (the page, or TagPreviewDialog's body) scrolls. A nested scroller swallowed touch
          drags on mobile, so the Apply buttons below it could not be reached. For the same reason
          the header is not sticky - it would stick under the app bar instead of inside a box.
          overflow-clip, not overflow-x-auto: this wrapper only needs to round the corners, and any
          non-visible overflow-x makes it a scroll container (overflow-y computes to auto). The
          table is table-fixed and wraps, so nothing needs to scroll sideways. */}
      <div className="border-border overflow-clip rounded-md border">
        {/* table-fixed + no phone minimums: the columns share the dialog's width instead of forcing it
            wider (the old min-widths made the grid ~370px, so on a phone the Use checkboxes
            scrolled out of view). Long values wrap; the wide sm: layout is unchanged. */}
        <table className="w-full table-fixed border-collapse text-left text-xs">
          <thead className="bg-muted/70 text-muted-foreground border-b">
            <tr>
              <th className="w-10 p-1.5 text-center sm:w-10 sm:p-2">Use</th>
              <th className="w-[4.5rem] p-1.5 sm:w-28 sm:p-2">Field</th>
              <th className="p-1.5 sm:min-w-[90px] sm:p-2">Current Value</th>
              <th className="p-1.5 sm:min-w-[120px] sm:p-2">New Value</th>
            </tr>
          </thead>
          <tbody className="divide-border divide-y">
            {orderedFields.map((field) => {
              const isChecked = selected.has(field.key);
              const isLinkOrCover = field.key === "www" || field.key === "cover";
              return (
                <tr
                  key={field.key}
                  className={
                    field.changed
                      ? "bg-muted/20 hover:bg-muted/40 font-medium"
                      : "text-muted-foreground hover:bg-muted/10 opacity-70"
                  }
                >
                  <td className="p-1.5 text-center sm:p-2">
                    <Checkbox
                      checked={isChecked}
                      onCheckedChange={() => onToggleField(field.key)}
                    />
                  </td>
                  <td className="text-foreground p-1.5 font-semibold break-words sm:p-2">
                    {field.label}
                  </td>
                  <td
                    className={`p-1.5 break-words sm:p-2 ${
                      isLinkOrCover
                        ? "text-[11px] break-all"
                        : "max-w-[150px] break-words sm:max-w-[200px]"
                    }`}
                  >
                    {field.currentValue || <span className="text-muted-foreground italic">—</span>}
                  </td>
                  <td
                    className={`p-1.5 break-words sm:p-2 ${
                      isLinkOrCover
                        ? "text-[11px] break-all"
                        : "max-w-[200px] break-words sm:max-w-[300px]"
                    }`}
                  >
                    {field.key === "series" && seriesChoice && seriesChoice.options.length > 1 ? (
                      <PrimarySeriesChooser {...seriesChoice} />
                    ) : (
                      <span
                        className={
                          field.changed ? "text-primary font-bold dark:text-emerald-400" : ""
                        }
                      >
                        {field.newValue || <span className="text-muted-foreground italic">—</span>}
                      </span>
                    )}
                    {field.key === "authors" &&
                      authorsChoice &&
                      authorsChoice.options.length > 1 && (
                        <div className="mt-2">
                          <MainAuthorChooser {...authorsChoice} />
                        </div>
                      )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}

export default MetadataFieldDiffTable;
