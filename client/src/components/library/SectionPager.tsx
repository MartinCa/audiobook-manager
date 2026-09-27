import { Button } from "@/components/ui/button";
import { PAGE_SIZE, type PageSizeOption } from "@/constants/paging";
import { PageSizeSelect } from "./PageSizeSelect";

interface SectionPagerProps {
  currentPage: number;
  pageCount: number;
  totalCount: number;
  onPageChange: (page: number) => void;
  /**
   * Rows per page, for the "Showing X–Y of Z" range. Defaults to the shared library `PAGE_SIZE`
   * (50), which is what every series/author detail section and the series/authors overview lists
   * page at. A surface paging at a different size (e.g. the 20-row browse/search tabs) passes its
   * own value so the displayed range stays accurate.
   */
  pageSize?: number;
  /**
   * Renders the rows-per-page dropdown next to the range text and reports the newly selected
   * size. Omitted entirely for a handful of pagers whose page size is fixed rather than backed by
   * the shared `usePageSize` hook (dialog pagers over a small bounded candidate list). The caller
   * owns resetting the current page to 0 - this component only reports the new size.
   */
  onPageSizeChange?: (size: PageSizeOption) => void;
  /**
   * Disables both buttons regardless of which page they'd move to - for a surface mid-fetch (a
   * loading page) or mid-mutation (BulkMissingBookMatchDialog's apply-in-progress), on top of the
   * ordinary first/last-page clamping.
   */
  disabled?: boolean;
}

/**
 * The single clamped prev/next pager shared by every paged list in the app: the series and
 * author detail sections, the series/authors overview lists, the library book list, the
 * search-result tabs, upcoming releases, discovered audiobooks, and the bulk-match/series-match/
 * refresh-pending dialogs. One control instead of each surface hand-rolling its own "Showing X-Y
 * of Z" + Previous/Next pair keeps the paging UI (and its clamping behavior) from drifting between
 * views the way the four owned-book lists' filtering once did.
 */
export function SectionPager({
  currentPage,
  pageCount,
  totalCount,
  onPageChange,
  pageSize = PAGE_SIZE,
  onPageSizeChange,
  disabled = false,
}: SectionPagerProps) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-2 border-t pt-3">
      <div className="flex flex-wrap items-center gap-3">
        <span className="text-muted-foreground text-xs">
          Showing {currentPage * pageSize + 1}–{Math.min((currentPage + 1) * pageSize, totalCount)}{" "}
          of {totalCount}
        </span>
        {onPageSizeChange && (
          <PageSizeSelect value={pageSize} onChange={onPageSizeChange} disabled={disabled} />
        )}
      </div>
      <div className="flex items-center gap-2">
        <Button
          size="sm"
          variant="outline"
          disabled={disabled || currentPage === 0}
          onClick={() => onPageChange(currentPage - 1)}
        >
          Previous
        </Button>
        <Button
          size="sm"
          variant="outline"
          disabled={disabled || currentPage >= pageCount - 1}
          onClick={() => onPageChange(currentPage + 1)}
        >
          Next
        </Button>
      </div>
    </div>
  );
}

export default SectionPager;
