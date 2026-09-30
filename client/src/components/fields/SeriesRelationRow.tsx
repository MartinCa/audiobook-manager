import { Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { SeriesField } from "@/components/fields/SeriesField";
import { SeriesPartHints } from "@/components/fields/SeriesPartHints";

export interface SeriesRelationValue {
  name: string;
  part: string;
}

export interface SeriesRelationRowProps {
  /** 1-based position among the additional series, for the accessible names. */
  position: number;
  value: SeriesRelationValue;
  onChange: (next: SeriesRelationValue) => void;
  /** Swaps this series with the book's primary one. */
  onMakePrimary: () => void;
  onRemove: () => void;
  qualifiers: readonly string[];
  currentBookId?: number;
  /** A validation message for this row (e.g. the series is listed twice). */
  error?: string;
}

/**
 * One additional (non-primary) series of a book: the same series typeahead with its
 * exact/similar/new hint, part input and part-conflict notes the primary series has, plus
 * actions to make it the primary or remove it. The primary series itself stays in BookEditForm's
 * own fields - it is the one that reaches the file's tags and path.
 */
export function SeriesRelationRow({
  position,
  value,
  onChange,
  onMakePrimary,
  onRemove,
  qualifiers,
  currentBookId,
  error,
}: SeriesRelationRowProps) {
  return (
    <div className="border-border space-y-1 rounded-md border p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="text-xs font-medium">Additional series {position}</span>
        <div className="flex items-center gap-1">
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={onMakePrimary}
            aria-label={`Make additional series ${position} the primary series`}
          >
            Make primary
          </Button>
          <Button
            type="button"
            variant="ghost"
            size="icon-sm"
            onClick={onRemove}
            aria-label={`Remove additional series ${position}`}
          >
            <Trash2 />
          </Button>
        </div>
      </div>

      <div className="flex flex-col gap-4 sm:flex-row">
        <SeriesField
          value={value.name}
          onChange={(name) => onChange({ ...value, name })}
          showLabel={false}
        />
        <div className="min-w-0 flex-1">
          <label className="mb-1 block text-xs font-medium">Series Part / Book #</label>
          <Input
            value={value.part}
            onChange={(e) => onChange({ ...value, part: e.target.value })}
            placeholder="e.g. 1 or 2.5"
            aria-label={`Part of additional series ${position}`}
          />
        </div>
      </div>

      {error && <p className="text-destructive text-xs">{error}</p>}

      <SeriesPartHints
        series={value.name}
        part={value.part}
        qualifiers={qualifiers}
        currentBookId={currentBookId}
      />
    </div>
  );
}
