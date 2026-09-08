import { useEffect } from "react";

/**
 * Keeps a paged list's raw page state inside [0, pageCount - 1] once the current response's
 * total is known. A total that shrinks while the user sits on a later page (an alignment, a
 * backfill, an ignore on the last row, an external scan) otherwise leaves the next fetch asking
 * for a page that no longer exists: it comes back empty, and the section renders a dead-end
 * heading with the pager gone. Correcting the state (rather than only clamping the display, as
 * the pager does) makes the next fetch land on a valid page too, so the section re-renders its
 * content instead of staying stuck.
 */
export function useClampedPage(
  page: number,
  pageCount: number,
  setPage: (page: number) => void,
): void {
  useEffect(() => {
    if (page > pageCount - 1) {
      setPage(pageCount - 1);
    }
  }, [page, pageCount, setPage]);
}
