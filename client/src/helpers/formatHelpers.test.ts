import { describe, it, expect } from "vitest";
import { formatDate, formatDateTime, formatDuration, formatFileSize } from "./formatHelpers";

describe("formatHelpers", () => {
  it("formats duration correctly", () => {
    expect(formatDuration(0)).toBe("0s");
    expect(formatDuration(45)).toBe("45s");
    expect(formatDuration(125)).toBe("2m 5s");
    expect(formatDuration(3665)).toBe("1h 1m 5s");
  });

  it("formats file size correctly", () => {
    expect(formatFileSize(0)).toBe("0 B");
    expect(formatFileSize(1024)).toBe("1.00 KB");
    expect(formatFileSize(1048576)).toBe("1.00 MB");
  });
});

describe("formatDate", () => {
  it("keeps a date-only ISO value on its calendar date in any timezone", () => {
    const localMidnight = new Date(2026, 6, 9);
    const expected = `${localMidnight.getFullYear()}-${String(localMidnight.getMonth() + 1).padStart(2, "0")}-${String(localMidnight.getDate()).padStart(2, "0")}`;
    expect(formatDate("2026-07-09")).toBe(expected);
  });

  it("formats a naive timestamp by its local calendar date", () => {
    expect(formatDate("2026-07-09T23:59:59")).toBe("2026-07-09");
  });

  it("returns an empty string for null, empty, and invalid values", () => {
    expect(formatDate(null)).toBe("");
    expect(formatDate(undefined)).toBe("");
    expect(formatDate("")).toBe("");
    expect(formatDate("2026-02-30")).toBe("");
    expect(formatDate("not a date")).toBe("");
  });
});

describe("formatDateTime", () => {
  it("formats a full date-time as yyyy-MM-dd HH:mm:ss (24-hour)", () => {
    expect(formatDateTime("2026-07-09T15:51:36")).toBe("2026-07-09 15:51:36");
  });

  it("uses zero-padded 24-hour time", () => {
    expect(formatDateTime("2026-07-09T04:05:06")).toBe("2026-07-09 04:05:06");
  });

  it("renders a UTC wire timestamp in the local timezone", () => {
    const wire = "2026-07-09T15:51:36Z";
    const rendered = formatDateTime(wire);
    expect(rendered).toMatch(/^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$/);
    expect(new Date(rendered).getTime()).toBe(new Date(wire).getTime());
  });

  it("returns an empty string for null, empty, and invalid values", () => {
    expect(formatDateTime(null)).toBe("");
    expect(formatDateTime(undefined)).toBe("");
    expect(formatDateTime("")).toBe("");
    expect(formatDateTime("not a date")).toBe("");
  });
});
