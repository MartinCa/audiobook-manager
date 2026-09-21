/**
 * A single filter dimension the bar renders. Kept generic (no series/author-only concepts) so
 * the same component composes for the series list, the authors list and (per the product ask) a
 * future book list - each page supplies its own field list and labels.
 */
export type FilterFieldDef =
  | { type: "tristate"; key: string; label: string; trueLabel: string; falseLabel: string }
  | {
      type: "numberRange";
      label: string;
      minKey: string;
      maxKey: string;
      min?: number;
      /**
       * Optional display-unit conversion for a field whose stored value's unit differs from what
       * the input shows (e.g. a duration filter stored in seconds, entered in minutes - Bug 7).
       * `toDisplay` converts the stored (filter/URL-state) value to what the input renders;
       * `toStored` converts the typed number back before it lands in filter/URL state. Both are
       * identity when the field carries no `unit` at all.
       */
      unit?: { toDisplay: (stored: number) => number; toStored: (display: number) => number };
    }
  | {
      type: "dateRange";
      label: string;
      afterKey: string;
      beforeKey: string;
      neverKey?: string;
      neverLabel?: string;
    }
  | {
      type: "multiselect";
      key: string;
      label: string;
      options: string[];
      /**
       * Display text per option value, for a field whose stored value isn't itself human-readable
       * (e.g. a language filter storing ISO codes) - falls back to the raw value when absent or
       * when a specific option has no entry.
       */
      optionLabels?: Record<string, string>;
      /**
       * Optional "select every real option" convenience shown as an extra item above the option
       * list (e.g. "Any supported" for a Matched source filter - Bug 6). Picking it sets the
       * field's value to every option in `options` except `excludeValues` (the synthetic
       * "Unsupported/None" bucket); picking it again clears the field entirely. Purely a
       * client-side selection shortcut - the value that lands in filter/URL state is still the
       * plain list of real option strings, so the backend needs no changes to understand it.
       */
      selectAllOption?: { label: string; excludeValues: string[] };
    };

/** Loosely typed so callers can pass their own (SeriesListFilters/AuthorListFilters/BookListFilters) shape. */
export type FilterValueMap = Record<string, boolean | number | string | string[] | undefined>;

export function tristateValue(v: boolean | number | string | string[] | undefined): string {
  return v === true ? "true" : v === false ? "false" : "any";
}

/**
 * Parses a numberRange input's raw text into the integer the backend's `int?` model binding
 * expects, dropping anything a fraction (`2.5`) or exponent notation (`1e3`) would otherwise
 * smuggle through - the server rejects a non-integer with an unexplained 400. `toStored` applies
 * a field's optional display-unit conversion (see `FilterFieldDef`'s `unit`, e.g. minutes ->
 * seconds for the duration filter - Bug 7) after parsing and before truncation, so the value that
 * lands in filter/URL state is always in the backend's unit regardless of what the input showed.
 */
export function toIntFilterValue(
  raw: string,
  toStored: (parsed: number) => number = (v) => v,
): number | undefined {
  if (raw === "") {
    return undefined;
  }
  const parsed = Math.trunc(Number(raw));
  return Number.isFinite(parsed) ? Math.trunc(toStored(parsed)) : undefined;
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
      // A numberRange field's values are always number|undefined - the wider FilterValueMap type
      // (shared with tristate/multiselect fields) is narrowed back here.
      const storedMin = values[field.minKey] as number | undefined;
      const storedMax = values[field.maxKey] as number | undefined;
      const toDisplay = field.unit?.toDisplay ?? ((v: number) => v);
      const min = storedMin === undefined ? undefined : toDisplay(storedMin);
      const max = storedMax === undefined ? undefined : toDisplay(storedMax);
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
    } else if (field.type === "multiselect") {
      const selected = values[field.key];
      if (Array.isArray(selected) && selected.length > 0) {
        const realOptions = field.selectAllOption
          ? field.options.filter((o) => !field.selectAllOption!.excludeValues.includes(o))
          : [];
        const isSelectAll =
          field.selectAllOption != null &&
          realOptions.length > 0 &&
          selected.length === realOptions.length &&
          realOptions.every((o) => selected.includes(o));
        const label = isSelectAll
          ? `${field.label}: ${field.selectAllOption!.label}`
          : `${field.label}: ${selected.map((v) => field.optionLabels?.[v] ?? v).join(", ")}`;
        chips.push({
          key: field.key,
          label,
          clear: () => ({ ...values, [field.key]: undefined }),
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
