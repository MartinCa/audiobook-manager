import { describe, it, expect } from "vitest";
import { formatDuration, formatFileSize } from "./formatHelpers";

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
