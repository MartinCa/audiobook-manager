import { describe, expect, it } from "vitest";
import { formatDateTime } from "@/helpers/formatHelpers";
import {
  activeChips,
  dateTimeLocalToIso,
  isoToDateTimeLocal,
  type FilterFieldDef,
} from "./filterUtils";

const FIELD: FilterFieldDef = {
  type: "dateRange",
  label: "Last refreshed",
  afterKey: "refreshedAfter",
  beforeKey: "refreshedBefore",
  neverKey: "neverRefreshed",
  neverLabel: "Never refreshed",
};

describe("dateTimeLocalToIso / isoToDateTimeLocal", () => {
  it("round-trips a typed local date-time through the stored UTC instant", () => {
    const iso = dateTimeLocalToIso("2026-01-15T10:30");

    expect(iso).toBe(new Date("2026-01-15T10:30").toISOString());
    expect(isoToDateTimeLocal(iso)).toBe("2026-01-15T10:30");
  });

  it("returns undefined for an empty or unparseable input", () => {
    expect(dateTimeLocalToIso("")).toBeUndefined();
    expect(dateTimeLocalToIso("not a date")).toBeUndefined();
  });

  it("renders nothing for a missing or unparseable stored value", () => {
    expect(isoToDateTimeLocal(undefined)).toBe("");
    expect(isoToDateTimeLocal("garbage")).toBe("");
  });

  it("still reads a legacy date-only stored value", () => {
    expect(isoToDateTimeLocal("2024-01-01")).toBe(
      isoToDateTimeLocal(new Date("2024-01-01").toISOString()),
    );
    expect(isoToDateTimeLocal("2024-01-01")).not.toBe("");
  });
});

describe("activeChips for a dateRange field", () => {
  const after = "2026-01-15T10:30:00.000Z";
  const before = "2026-02-01T08:00:00.000Z";

  it("labels an after-only bound with the exact local date-time", () => {
    const chips = activeChips([FIELD], { refreshedAfter: after });
    expect(chips.map((c) => c.label)).toEqual([`Last refreshed: after ${formatDateTime(after)}`]);
  });

  it("labels a before-only bound with the exact local date-time", () => {
    const chips = activeChips([FIELD], { refreshedBefore: before });
    expect(chips.map((c) => c.label)).toEqual([`Last refreshed: before ${formatDateTime(before)}`]);
  });

  it("labels a full range", () => {
    const chips = activeChips([FIELD], { refreshedAfter: after, refreshedBefore: before });
    expect(chips.map((c) => c.label)).toEqual([
      `Last refreshed: ${formatDateTime(after)} – ${formatDateTime(before)}`,
    ]);
  });

  it("labels never-refreshed ahead of any bounds", () => {
    const chips = activeChips([FIELD], { neverRefreshed: true });
    expect(chips.map((c) => c.label)).toEqual(["Last refreshed: Never refreshed"]);
  });
});
