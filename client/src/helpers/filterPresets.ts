import type { FilterValueMap } from "@/components/filters/filterUtils";

/** The shapes a list filter can take - the same ones the lists keep in their route search params. */
export type FilterValue = boolean | number | string | string[];

/** A preset's filters: only filters that are set, each with a real value. */
export type PresetFilters = Record<string, FilterValue>;

function isFilterValue(value: unknown): value is FilterValue {
  return (
    typeof value === "boolean" ||
    typeof value === "string" ||
    (typeof value === "number" && Number.isFinite(value)) ||
    (Array.isArray(value) && value.every((item) => typeof item === "string"))
  );
}

/**
 * The filters that are actually set, in the shape a preset stores them: `undefined` (cleared) and
 * empty lists are dropped, while `false` is kept - "Not followed" is a real choice. The search
 * text is not a filter and never part of what is passed here.
 */
export function activeFilterValues(filters: FilterValueMap): PresetFilters {
  const active: PresetFilters = {};
  for (const [key, value] of Object.entries(filters)) {
    if (value === undefined || (Array.isArray(value) && value.length === 0)) continue;
    active[key] = value;
  }
  return active;
}

/**
 * Reads a stored preset back into filter values. The server stores whatever it validated, but the
 * wire type is an open bag, so anything that is not a filter value shape is dropped rather than
 * trusted into the route's search params.
 */
export function readPresetFilters(raw: Record<string, unknown>): PresetFilters {
  const filters: PresetFilters = {};
  for (const [key, value] of Object.entries(raw)) {
    if (isFilterValue(value)) filters[key] = value;
  }
  return activeFilterValues(filters);
}

function canonical(value: FilterValue): string {
  // A list filter is a set (the server treats it as "any of these"), so order is not part of it.
  return Array.isArray(value) ? JSON.stringify([...value].sort()) : JSON.stringify(value);
}

/** Whether two filter sets say the same thing, ignoring key order, list order and cleared keys. */
export function sameFilters(a: FilterValueMap, b: FilterValueMap): boolean {
  const left = activeFilterValues(a);
  const right = activeFilterValues(b);
  const keys = Object.keys(left);
  return (
    keys.length === Object.keys(right).length &&
    keys.every((key) => key in right && canonical(left[key]!) === canonical(right[key]!))
  );
}

/**
 * The value map that makes a list show exactly the preset: every filter that is currently set is
 * cleared first (a preset replaces the filters, it does not layer on top of them), then the
 * preset's own are applied. The result keeps the cleared keys as `undefined`, which is how the
 * lists' route handlers drop a search param.
 */
export function applyPresetTo(current: FilterValueMap, preset: PresetFilters): FilterValueMap {
  const next: FilterValueMap = {};
  for (const key of Object.keys(current)) next[key] = undefined;
  return { ...next, ...preset };
}
