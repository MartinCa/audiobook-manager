import { describe, it, expect } from "vitest";
import { formatVersion, getReleaseUrl } from "./versionHelpers";

describe("versionHelpers", () => {
  describe("formatVersion", () => {
    it("returns 'dev' for dev or empty strings", () => {
      expect(formatVersion("dev")).toBe("dev");
      expect(formatVersion("")).toBe("dev");
    });

    it("prefixes non-v versions with v", () => {
      expect(formatVersion("0.9.0")).toBe("v0.9.0");
      expect(formatVersion("1.2.3-beta.1")).toBe("v1.2.3-beta.1");
    });

    it("preserves versions that already start with v", () => {
      expect(formatVersion("v0.9.0")).toBe("v0.9.0");
    });
  });

  describe("getReleaseUrl", () => {
    it("returns main repo url for dev version", () => {
      expect(getReleaseUrl("dev")).toBe("https://github.com/MartinCa/audiobook-manager");
    });

    it("returns release tag url for release versions", () => {
      expect(getReleaseUrl("0.9.0")).toBe(
        "https://github.com/MartinCa/audiobook-manager/releases/tag/v0.9.0",
      );
      expect(getReleaseUrl("v1.0.0")).toBe(
        "https://github.com/MartinCa/audiobook-manager/releases/tag/v1.0.0",
      );
    });
  });
});
