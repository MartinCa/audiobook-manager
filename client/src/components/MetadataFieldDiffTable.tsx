import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import type { FieldDiff } from "@/hooks/useMetadataFieldDiffs";

interface MetadataFieldDiffTableProps {
  fields: FieldDiff[];
  selected: Set<string>;
  onToggleField: (key: string) => void;
  onToggleAll: () => void;
  changedFieldKeys: string[];
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
}: MetadataFieldDiffTableProps) {
  return (
    <div className="space-y-2">
      <div className="flex flex-col justify-between gap-2 sm:flex-row sm:items-center">
        <Button
          variant="ghost"
          size="sm"
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

      <div className="border-border max-h-[50vh] overflow-x-auto overflow-y-auto rounded-md border">
        <table className="w-full border-collapse text-left text-xs">
          <thead className="bg-muted/70 text-muted-foreground sticky top-0 z-10 border-b">
            <tr>
              <th className="w-8 p-2 text-center sm:w-10">Use</th>
              <th className="w-20 p-2 sm:w-28">Field</th>
              <th className="min-w-[90px] p-2">Current Value</th>
              <th className="min-w-[120px] p-2">New Value</th>
            </tr>
          </thead>
          <tbody className="divide-border divide-y">
            {fields.map((field) => {
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
                  <td className="p-2 text-center">
                    <Checkbox
                      checked={isChecked}
                      onCheckedChange={() => onToggleField(field.key)}
                    />
                  </td>
                  <td className="text-foreground p-2 font-semibold">{field.label}</td>
                  <td
                    className={`p-2 ${
                      isLinkOrCover
                        ? "text-[11px] break-all"
                        : "max-w-[150px] break-words sm:max-w-[200px]"
                    }`}
                  >
                    {field.currentValue || <span className="text-muted-foreground italic">—</span>}
                  </td>
                  <td
                    className={`p-2 ${
                      isLinkOrCover
                        ? "text-[11px] break-all"
                        : "max-w-[200px] break-words sm:max-w-[300px]"
                    }`}
                  >
                    <span
                      className={
                        field.changed ? "text-primary font-bold dark:text-emerald-400" : ""
                      }
                    >
                      {field.newValue || <span className="text-muted-foreground italic">—</span>}
                    </span>
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
