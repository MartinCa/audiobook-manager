import { CalendarDays, X } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

/**
 * A single filter dimension the bar renders. Kept generic (no series/author-only concepts) so
 * the same component composes for the series list, the authors list and (per the product ask) a
 * future book list - each page supplies its own field list and labels.
 */
export type FilterFieldDef =
  | { type: "tristate"; key: string; label: string; trueLabel: string; falseLabel: string }
  | { type: "numberRange"; label: string; minKey: string; maxKey: string; min?: number }
  | {
      type: "dateRange";
      label: string;
      afterKey: string;
      beforeKey: string;
      neverKey?: string;
      neverLabel?: string;
    };

/** Loosely typed so callers can pass their own (SeriesListFilters/AuthorListFilters) shape. */
export type FilterValueMap = Record<string, boolean | number | string | undefined>;

interface EntityFilterBarProps {
  fields: FilterFieldDef[];
  values: FilterValueMap;
  onChange: (next: FilterValueMap) => void;
}

function tristateValue(v: boolean | number | string | undefined): string {
  return v === true ? "true" : v === false ? "false" : "any";
}

/** Every field that currently carries a value, as a removable summary chip. */
function activeChips(fields: FilterFieldDef[], values: FilterValueMap) {
  const chips: { key: string; label: string; clear: () => FilterValueMap }[] = [];

  for (const field of fields) {
    if (field.type === "tristate") {
      const v = values[field.key];
      if (v !== undefined) {
        chips.push({
          key: field.key,
          label: `${field.label}: ${v ? field.trueLabel : field.falseLabel}`,
          clear: () => ({ ...values, [field.key]: undefined }),
        });
      }
    } else if (field.type === "numberRange") {
      const min = values[field.minKey];
      const max = values[field.maxKey];
      if (min !== undefined || max !== undefined) {
        const label =
          min !== undefined && max !== undefined
            ? `${field.label}: ${min}–${max}`
            : min !== undefined
              ? `${field.label}: ≥ ${min}`
              : `${field.label}: ≤ ${max}`;
        chips.push({
          key: field.minKey,
          label,
          clear: () => ({ ...values, [field.minKey]: undefined, [field.maxKey]: undefined }),
        });
      }
    } else {
      const after = values[field.afterKey];
      const before = values[field.beforeKey];
      const never = field.neverKey ? values[field.neverKey] : undefined;
      if (never === true) {
        chips.push({
          key: field.afterKey,
          label: `${field.label}: ${field.neverLabel ?? "Never"}`,
          clear: () => ({
            ...values,
            [field.afterKey]: undefined,
            [field.beforeKey]: undefined,
            ...(field.neverKey ? { [field.neverKey]: undefined } : {}),
          }),
        });
      } else if (after !== undefined || before !== undefined) {
        const label =
          after !== undefined && before !== undefined
            ? `${field.label}: ${String(after)} – ${String(before)}`
            : after !== undefined
              ? `${field.label}: after ${String(after)}`
              : `${field.label}: before ${String(before)}`;
        chips.push({
          key: field.afterKey,
          label,
          clear: () => ({ ...values, [field.afterKey]: undefined, [field.beforeKey]: undefined }),
        });
      }
    }
  }

  return chips;
}

/**
 * A row of filter controls plus a summary of active filters as dismissible chips. Purely
 * props-driven: it holds no state and no API knowledge - the caller owns the filter values (in
 * this app, the route's search params, exactly like the existing `q` search term) and receives
 * the next value map on every change.
 */
export function EntityFilterBar({ fields, values, onChange }: EntityFilterBarProps) {
  const chips = activeChips(fields, values);

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-end gap-3">
        {fields.map((field) => {
          if (field.type === "tristate") {
            return (
              <div key={field.key} className="space-y-1">
                <label className="text-muted-foreground text-xs font-semibold uppercase">
                  {field.label}
                </label>
                <Select
                  value={tristateValue(values[field.key])}
                  onValueChange={(v) =>
                    onChange({ ...values, [field.key]: v === "any" ? undefined : v === "true" })
                  }
                  items={[
                    { value: "any", label: "Any" },
                    { value: "true", label: field.trueLabel },
                    { value: "false", label: field.falseLabel },
                  ]}
                >
                  <SelectTrigger size="sm" className="w-36" aria-label={field.label}>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="any">Any</SelectItem>
                    <SelectItem value="true">{field.trueLabel}</SelectItem>
                    <SelectItem value="false">{field.falseLabel}</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            );
          }

          if (field.type === "numberRange") {
            return (
              <div key={field.minKey} className="space-y-1">
                <label className="text-muted-foreground text-xs font-semibold uppercase">
                  {field.label}
                </label>
                <div className="flex items-center gap-1">
                  <Input
                    type="number"
                    inputMode="numeric"
                    min={field.min ?? 0}
                    placeholder="Min"
                    value={values[field.minKey] === undefined ? "" : String(values[field.minKey])}
                    onChange={(e) =>
                      onChange({
                        ...values,
                        [field.minKey]: e.target.value === "" ? undefined : Number(e.target.value),
                      })
                    }
                    className="w-20"
                    aria-label={`${field.label} minimum`}
                  />
                  <span className="text-muted-foreground text-xs">–</span>
                  <Input
                    type="number"
                    inputMode="numeric"
                    min={field.min ?? 0}
                    placeholder="Max"
                    value={values[field.maxKey] === undefined ? "" : String(values[field.maxKey])}
                    onChange={(e) =>
                      onChange({
                        ...values,
                        [field.maxKey]: e.target.value === "" ? undefined : Number(e.target.value),
                      })
                    }
                    className="w-20"
                    aria-label={`${field.label} maximum`}
                  />
                </div>
              </div>
            );
          }

          const neverActive = field.neverKey ? values[field.neverKey] === true : false;
          return (
            <div key={field.afterKey} className="space-y-1">
              <label className="text-muted-foreground text-xs font-semibold uppercase">
                {field.label}
              </label>
              <div className="flex flex-wrap items-center gap-1">
                <div className="relative">
                  <CalendarDays className="text-muted-foreground pointer-events-none absolute top-1/2 left-2 h-3.5 w-3.5 -translate-y-1/2" />
                  <Input
                    type="date"
                    disabled={neverActive}
                    value={
                      values[field.afterKey] === undefined ? "" : String(values[field.afterKey])
                    }
                    onChange={(e) =>
                      onChange({ ...values, [field.afterKey]: e.target.value || undefined })
                    }
                    className="w-36 pl-7 text-xs"
                    aria-label={`${field.label} after`}
                  />
                </div>
                <span className="text-muted-foreground text-xs">–</span>
                <div className="relative">
                  <CalendarDays className="text-muted-foreground pointer-events-none absolute top-1/2 left-2 h-3.5 w-3.5 -translate-y-1/2" />
                  <Input
                    type="date"
                    disabled={neverActive}
                    value={
                      values[field.beforeKey] === undefined ? "" : String(values[field.beforeKey])
                    }
                    onChange={(e) =>
                      onChange({ ...values, [field.beforeKey]: e.target.value || undefined })
                    }
                    className="w-36 pl-7 text-xs"
                    aria-label={`${field.label} before`}
                  />
                </div>
                {field.neverKey ? (
                  <Button
                    type="button"
                    size="sm"
                    variant={neverActive ? "default" : "outline"}
                    className="h-8"
                    onClick={() =>
                      onChange({
                        ...values,
                        [field.neverKey!]: neverActive ? undefined : true,
                        [field.afterKey]: undefined,
                        [field.beforeKey]: undefined,
                      })
                    }
                  >
                    {field.neverLabel ?? "Never"}
                  </Button>
                ) : null}
              </div>
            </div>
          );
        })}
      </div>

      {chips.length > 0 && (
        <div className="flex flex-wrap items-center gap-1.5">
          {chips.map((chip) => (
            <Badge key={chip.key} variant="secondary" className="gap-1 pr-1">
              {chip.label}
              <button
                type="button"
                onClick={() => onChange(chip.clear())}
                aria-label={`Remove filter: ${chip.label}`}
                className="hover:bg-muted-foreground/20 rounded-full p-0.5"
              >
                <X className="h-3 w-3" />
              </button>
            </Badge>
          ))}
          <Button
            type="button"
            variant="ghost"
            size="sm"
            className="h-5 px-1.5 text-xs"
            onClick={() => {
              const cleared: FilterValueMap = { ...values };
              for (const key of Object.keys(cleared)) {
                cleared[key] = undefined;
              }
              onChange(cleared);
            }}
          >
            Clear all
          </Button>
        </div>
      )}
    </div>
  );
}

export default EntityFilterBar;
