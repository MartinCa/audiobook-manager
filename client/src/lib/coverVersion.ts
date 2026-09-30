// A cover is rewritten in place on save, so its URL never changes. Browsers keep decoded images
// in an in-memory cache keyed by URL for the life of the page and reuse them for a remounted
// <img> without revalidating (the server's `no-cache` only applies to a real fetch), so an
// in-app navigation after a cover change showed the old picture until a full reload. Bumping this
// version after a save gives the next render a new URL and forces a fetch.
let version = 0;

export function bumpCoverVersion(): void {
  version = Date.now();
}

export function withCoverVersion(url: string): string {
  if (version === 0) return url;
  return `${url}${url.includes("?") ? "&" : "?"}v=${version}`;
}
