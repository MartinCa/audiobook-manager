import { formatDateTime } from "@/helpers/formatHelpers";

/**
 * The shared "last refreshed from source" indicator, aligned with the book detail's display
 * ("Last refreshed from source: 2026-09-15 12:34:56" / "Never"). Used by the series detail and
 * the pending series-refresh list so a book and a series answer the same question the same way.
 */
export function LastRefreshedHint({
  lastRefreshedAt,
}: {
  lastRefreshedAt: string | null | undefined;
}) {
  return (
    <span>
      Last refreshed from source: {lastRefreshedAt ? formatDateTime(lastRefreshedAt) : "Never"}
    </span>
  );
}
