export function formatVersion(version: string): string {
  if (!version || version === "dev") return "dev";
  return version.startsWith("v") ? version : `v${version}`;
}

export function getReleaseUrl(version: string): string {
  if (!version || version === "dev") {
    return "https://github.com/MartinCa/audiobook-manager";
  }
  const tag = formatVersion(version);
  return `https://github.com/MartinCa/audiobook-manager/releases/tag/${tag}`;
}
