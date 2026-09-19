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

export function tristateValue(v: boolean | number | string | undefined): string {
  return v === true ? "true" : v === false ? "false" : "any";
}

/**
 * Parses a numberRange input's raw text into the integer the backend's `int?` model binding
 * expects, dropping anything a fraction (`2.5`) or exponent notation (`1e3`) would otherwise
 * smuggle through - the server rejects a non-integer with an unexplained 400.
 */
export function toIntFilterValue(raw: string): number | undefined {
  if (raw === "") {
    return undefined;
  }
  const parsed = Math.trunc(Number(raw));
  return Number.isFinite(parsed) ? parsed : undefined;
}

/** Every field that currently carries a value, as a removable summary chip. */
export function activeChips(fields: FilterFieldDef[], values: FilterValueMap) {
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
 * How many filter dimensions currently carry a value - drives the badge on the collapsed-state
 * toggle button (see `FilterToggleButton`) so a caller can tell there's an active filter without
 * expanding the bar.
 */
export function countActiveFilters(fields: FilterFieldDef[], values: FilterValueMap): number {
  return activeChips(fields, values).length;
}
