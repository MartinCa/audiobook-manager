import { useState } from "react";
import { Link } from "@tanstack/react-router";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowLeft,
  Users,
  BookMarked,
  BookOpen,
  Loader2,
  ExternalLink,
  RefreshCw,
  CheckCircle2,
  ChevronRight,
  Unplug,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { PAGE_SIZE } from "@/constants/paging";
import { OwnedBookList } from "./OwnedBookList";
import { SeriesListEntry } from "./SeriesListEntry";
import { AuthorFollowSection } from "./AuthorFollowSection";
import { UpcomingReleasesList } from "./UpcomingReleasesList";
import { ExpectedBookList } from "./ExpectedBookList";
import { SectionPager } from "./SectionPager";
import { LinkButton } from "../LinkButton";
import { CollapsibleCountSection } from "@/components/CollapsibleCountSection";
import { LastRefreshedHint } from "@/components/LastRefreshedHint";
import { browseApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { useClampedPage } from "@/hooks/useClampedPage";
import { useBookSelection } from "@/hooks/useBookSelection";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";
import type { AuthorExpectedBook, AuthorMissingSeries } from "@/types/AuthorDetail";
import { hasActiveFilters, type BookListFilters } from "@/types/EntityFilters";
import { Route } from "@/routes/library/authors/$authorId";

export function AuthorDetail() {
  const { authorId } = Route.useParams();
  const id = Number(authorId);
  const queryClient = useQueryClient();

  // Each section pages server-side; each has its own page state so paging one section doesn't
  // move the others. The unpaged version sent an author's entire catalogue at once. The
  // missing-series section is opt-in on the endpoint (includeMissingSeries) - the backend only
  // pays for its reconciliation pass when a caller wants the section.
  const [seriesPage, setSeriesPage] = useState(0);
  const [standalonePage, setStandalonePage] = useState(0);
  const [missingSeriesPage, setMissingSeriesPage] = useState(0);
  const selection = useBookSelection();
  const [refreshing, setRefreshing] = useState(false);
  const [ignoringBookId, setIgnoringBookId] = useState<number | null>(null);
  const [showIgnored, setShowIgnored] = useState(false);
  // Standalone-books text search and option filters (Bug 8 unification): local component state
  // rather than a route search param, since this section's filtering is scoped to one author's
  // page and never needs to be shareable via URL the way the library list's does.
  const [standaloneSearch, setStandaloneSearch] = useState("");
  const [standaloneFilters, setStandaloneFilters] = useState<BookListFilters>({});

  // Navigating between authors must not carry a previous author's page cursor along. Adjusted
  // during render (React's documented pattern) rather than in an effect: the query key below
  // already changes with the author, so this only resets the local paging state when it does.
  // The book selection resets for the same reason - a different author owns a different roster.
  const [prevId, setPrevId] = useState(id);
  if (prevId !== id) {
    setPrevId(id);
    setSeriesPage(0);
    setStandalonePage(0);
    setMissingSeriesPage(0);
    setShowIgnored(false);
    selection.clear();
    setStandaloneSearch("");
    setStandaloneFilters({});
  }

  // One combined detail query instead of two: the endpoint already computes both sections on
  // every call and accepts both sections' cursors, so separate queries made every section
  // change issue an extra backend call whose other section (computed with default paging) was
  // thrown away. keepPreviousData keeps both sections rendered while one of them pages.
  const detailQuery = useQuery({
    queryKey: queryKeys.author.detail(
      id,
      seriesPage,
      standalonePage,
      missingSeriesPage,
      standaloneSearch,
      standaloneFilters,
    ),
    queryFn: () =>
      browseApi.getAuthorDetail(id, {
        seriesLimit: PAGE_SIZE,
        seriesOffset: seriesPage * PAGE_SIZE,
        standaloneLimit: PAGE_SIZE,
        standaloneOffset: standalonePage * PAGE_SIZE,
        includeMissingSeries: true,
        missingSeriesLimit: PAGE_SIZE,
        missingSeriesOffset: missingSeriesPage * PAGE_SIZE,
        standaloneSearch,
        standaloneFilters,
      }),
    enabled: Boolean(id),
    placeholderData: keepPreviousData,
  });

  const handleStandaloneSearchChange = (next: string) => {
    setStandaloneSearch(next);
    setStandalonePage(0);
  };

  const handleStandaloneFiltersChange = (next: BookListFilters) => {
    setStandaloneFilters(next);
    setStandalonePage(0);
  };

  const author = detailQuery.data?.author;
  const seriesSection = detailQuery.data?.series ?? { items: [], total: 0 };
  const standaloneSection = detailQuery.data?.standaloneBooks ?? { items: [], total: 0 };
  const missingBooks = detailQuery.data?.missingBooks ?? [];
  const upcomingBooks = detailQuery.data?.upcomingBooks ?? [];
  const ignoredBooks = detailQuery.data?.ignoredBooks ?? [];
  const missingSeriesSection = detailQuery.data?.missingSeries ?? { items: [], total: 0 };

  const seriesPageCount = Math.max(1, Math.ceil(seriesSection.total / PAGE_SIZE));
  const standalonePageCount = Math.max(1, Math.ceil(standaloneSection.total / PAGE_SIZE));
  const missingSeriesPageCount = Math.max(1, Math.ceil(missingSeriesSection.total / PAGE_SIZE));
  // Clamped so the fetched and the displayed page can never disagree.
  const currentSeriesPage = Math.min(seriesPage, seriesPageCount - 1);
  const currentStandalonePage = Math.min(standalonePage, standalonePageCount - 1);
  const currentMissingSeriesPage = Math.min(missingSeriesPage, missingSeriesPageCount - 1);

  // And the raw page states are pulled back into range once a response shows a section shrank
  // under them (e.g. books moved between sections from another tab), so the next fetch - not
  // just the display - lands on a valid page.
  useClampedPage(seriesPage, seriesPageCount, setSeriesPage);
  useClampedPage(standalonePage, standalonePageCount, setStandalonePage);
  useClampedPage(missingSeriesPage, missingSeriesPageCount, setMissingSeriesPage);

  // The Hardcover match, for the management section's badge - the author's own follow/match
  // dialog lives in AuthorFollowSection; this is a read-only display of the same match.
  const matchQuery = useQuery({
    queryKey: queryKeys.authorHardcoverMatch(id),
    queryFn: () => browseApi.getAuthorHardcoverMatch(id),
    enabled: Boolean(id),
  });
  const isMatched = Boolean(matchQuery.data?.sourceId);

  const invalidateDetail = () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.author.all() });
  };

  const handleRefresh = async () => {
    setRefreshing(true);
    try {
      await browseApi.refreshAuthor(id);
      notifications.success("Refreshed bibliography from source");
      invalidateDetail();
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setRefreshing(false);
    }
  };

  // Mirrors SeriesDetail's ignore/unignore pair: an author's roster entry can now be unignored
  // from the same list (the "show ignored" toggle), so a misclick is recoverable. The stable
  // expected-book row id (book.id) names the exact shared row - the title route cannot tell two
  // same-titled entries apart.
  const handleSetIgnored = async (book: AuthorExpectedBook, ignored: boolean) => {
    setIgnoringBookId(book.id);
    try {
      if (ignored) {
        await browseApi.ignoreAuthorExpectedBook(id, { id: book.id });
        notifications.success(`Ignored "${book.title}"`);
      } else {
        await browseApi.unignoreAuthorExpectedBook(id, { id: book.id });
        notifications.success(`Unignored "${book.title}"`);
      }
      invalidateDetail();
      // The upcoming-releases view renders the same shared row with the flag applied, so a
      // dismissal there must not keep a stale entry for the query cache's TTL.
      void queryClient.invalidateQueries({ queryKey: queryKeys.upcomingReleases.all() });
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setIgnoringBookId(null);
    }
  };

  if (!author && detailQuery.isLoading) {
    return (
      <div className="text-muted-foreground flex flex-col items-center justify-center py-20">
        <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
        <p className="text-sm">Loading author details...</p>
      </div>
    );
  }

  if (!author) {
    return (
      <div className="space-y-4 py-12 text-center">
        <h2 className="text-xl font-bold">Author not found</h2>
        <LinkButton render={<Link to="/library/authors" />}>Back to Authors</LinkButton>
      </div>
    );
  }

  const series = seriesSection.items;
  const standaloneBooks = standaloneSection.items;
  // Keeps the standalone section (and its search/filter bar) visible when a search or filter
  // narrows the section to zero results, rather than hiding the only way to clear it. An author
  // with no standalone books at all, and no active search/filter, still hides the section.
  const hasActiveStandaloneSearchOrFilter =
    standaloneSearch.trim() !== "" || hasActiveFilters(standaloneFilters);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <LinkButton variant="ghost" size="sm" render={<Link to="/library/authors" />}>
          <ArrowLeft className="mr-2 h-4 w-4" />
          Back to Authors
        </LinkButton>
      </div>

      <div className="border-border border-b pb-4">
        <h1 className="text-foreground flex items-center gap-2 text-2xl font-bold">
          <Users className="text-primary h-6 w-6" />
          {author.name}
        </h1>
        <p className="text-muted-foreground text-sm">
          {author.bookCount} {author.bookCount === 1 ? "audiobook" : "audiobooks"} in library
        </p>
        <div className="mt-3">
          <AuthorFollowSection authorId={author.id} authorName={author.name} />
        </div>
      </div>

      <UpcomingReleasesList
        authorId={author.id}
        sectionTitle={
          <>
            <BookMarked className="text-primary h-5 w-5" />
            Upcoming Releases
          </>
        }
        emptyMessage="No upcoming releases tracked for this author yet."
      />

      {seriesSection.total > 0 && (
        <div className="space-y-3">
          <h2 className="text-foreground flex items-center gap-2 text-lg font-bold">
            <BookMarked className="text-primary h-5 w-5" />
            Series ({seriesSection.total})
          </h2>
          <div className="space-y-2">
            {series.map((s) => (
              <SeriesListEntry key={s.name} series={s} search={{ authorId: author.id }} />
            ))}
          </div>
          {seriesPageCount > 1 && (
            <div className="flex flex-wrap items-center justify-between gap-2">
              <span className="text-muted-foreground text-xs">
                Showing {currentSeriesPage * PAGE_SIZE + 1}–
                {Math.min((currentSeriesPage + 1) * PAGE_SIZE, seriesSection.total)} of{" "}
                {seriesSection.total}
              </span>
              <div className="flex items-center gap-2">
                <Button
                  size="sm"
                  variant="outline"
                  disabled={currentSeriesPage === 0}
                  onClick={() => setSeriesPage(currentSeriesPage - 1)}
                >
                  Previous
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  disabled={currentSeriesPage >= seriesPageCount - 1}
                  onClick={() => setSeriesPage(currentSeriesPage + 1)}
                >
                  Next
                </Button>
              </div>
            </div>
          )}
        </div>
      )}

      {(standaloneSection.total > 0 || hasActiveStandaloneSearchOrFilter) && (
        <div className="space-y-3">
          <h2 className="text-foreground flex items-center gap-2 text-lg font-bold">
            <BookOpen className="text-primary h-5 w-5" />
            Standalone Audiobooks ({standaloneSection.total})
          </h2>
          <OwnedBookList
            books={standaloneBooks}
            totalCount={standaloneSection.total}
            emptyState={
              <p className="text-muted-foreground text-sm">No standalone books matched.</p>
            }
            selection={selection}
            search={standaloneSearch}
            onSearchChange={handleStandaloneSearchChange}
            filters={standaloneFilters}
            onFiltersChange={handleStandaloneFiltersChange}
            page={currentStandalonePage}
            pageCount={standalonePageCount}
            pageSize={PAGE_SIZE}
            onPageChange={setStandalonePage}
            itemNoun="books"
          />
        </div>
      )}

      {missingSeriesSection.total > 0 && (
        <CollapsibleCountSection
          label="Missing Series"
          count={missingSeriesSection.total}
          defaultOpen={false}
        >
          <p className="text-muted-foreground text-xs">
            Series from this author's matched bibliography that your library owns no book in yet.
          </p>
          <div className="space-y-2">
            {missingSeriesSection.items.map((ms) => (
              <MissingSeriesRow
                key={`${ms.sourceName}:${ms.sourceSeriesId}`}
                series={ms}
                authorId={author.id}
              />
            ))}
          </div>
          {missingSeriesPageCount > 1 && (
            <SectionPager
              currentPage={currentMissingSeriesPage}
              pageCount={missingSeriesPageCount}
              totalCount={missingSeriesSection.total}
              onPageChange={setMissingSeriesPage}
            />
          )}
        </CollapsibleCountSection>
      )}

      {ignoredBooks.length > 0 && (
        <div className="flex flex-wrap items-center gap-2 pt-1">
          <Checkbox
            id="show-ignored-books"
            checked={showIgnored}
            onCheckedChange={(checked) => setShowIgnored(Boolean(checked))}
          />
          <label
            htmlFor="show-ignored-books"
            className="text-muted-foreground cursor-pointer text-xs leading-none select-none"
          >
            Show ignored books ({ignoredBooks.length})
          </label>
        </div>
      )}

      <CollapsibleCountSection
        label="Missing Books"
        count={missingBooks.length}
        labelClassName="text-amber-600 dark:text-amber-400"
      >
        <ExpectedBookList
          section="missing"
          items={missingBooks}
          ignoredItems={ignoredBooks}
          ignoredTotal={ignoredBooks.length}
          showIgnored={showIgnored}
          busyBookId={ignoringBookId}
          emptyMessage="No missing books detected for this author."
          onIgnore={(book) => void handleSetIgnored(book, true)}
          onUnignore={(book) => void handleSetIgnored(book, false)}
        />
      </CollapsibleCountSection>

      <CollapsibleCountSection
        label="Upcoming Books"
        count={upcomingBooks.length}
        labelClassName="text-muted-foreground"
      >
        <ExpectedBookList
          section="upcoming"
          items={upcomingBooks}
          ignoredItems={ignoredBooks}
          ignoredTotal={ignoredBooks.length}
          showIgnored={showIgnored}
          busyBookId={ignoringBookId}
          emptyMessage="No upcoming books detected for this author."
          onIgnore={(book) => void handleSetIgnored(book, true)}
          onUnignore={(book) => void handleSetIgnored(book, false)}
        />
      </CollapsibleCountSection>

      {/* Management & Settings: mirrors SeriesDetail's card, minus mapping-pattern CRUD and the
          danger zone (an author cannot be deleted here). */}
      <Card>
        <CardHeader className="py-3">
          <CardTitle className="text-muted-foreground text-sm font-semibold uppercase">
            Management &amp; Settings
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-4 text-xs">
          <div className="flex flex-col gap-3 sm:flex-row sm:flex-wrap sm:items-center">
            {isMatched && matchQuery.data ? (
              <>
                <Badge
                  variant="secondary"
                  className="bg-emerald-500/15 text-emerald-600 dark:text-emerald-400"
                >
                  <CheckCircle2 className="mr-1 h-3.5 w-3.5" />
                  Matched to{" "}
                  {matchQuery.data.sourceUrl ? (
                    <a
                      href={matchQuery.data.sourceUrl}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="text-primary ml-1 font-semibold hover:underline"
                    >
                      {matchQuery.data.sourceName}
                      <ExternalLink className="ml-1 inline h-3 w-3" />
                    </a>
                  ) : (
                    <span className="text-foreground ml-1 font-semibold">
                      {matchQuery.data.sourceName}
                    </span>
                  )}
                </Badge>
                <span className="text-muted-foreground">
                  <LastRefreshedHint lastRefreshedAt={detailQuery.data?.lastRefreshedAt} />
                </span>
                <Button
                  variant="outline"
                  size="sm"
                  className="w-full sm:w-auto"
                  disabled={refreshing}
                  onClick={() => {
                    void handleRefresh();
                  }}
                >
                  <RefreshCw className={`mr-1.5 h-4 w-4 ${refreshing ? "animate-spin" : ""}`} />
                  Refresh Online
                </Button>
              </>
            ) : (
              <p className="text-muted-foreground">
                Not matched to an online metadata provider yet. Use "Match to Hardcover" above to
                associate this author and enable refreshing their expected-book roster.
              </p>
            )}
          </div>
        </CardContent>
      </Card>
    </div>
  );
}

/** One row of the author detail's Missing Series section: the series name (the matched local
 * name, or the source's spelling while unmatched), its missing/upcoming/owned counts, and a link
 * to the local series detail once a series is matched. An unmatched series has no optics here
 * for matching it - the series match flow lives on the series' own detail page (SeriesDetail's
 * management card is not reusable as a per-row dialog), so the row renders a disabled hint
 * instead, pointing there. */
function MissingSeriesRow({ series, authorId }: { series: AuthorMissingSeries; authorId: number }) {
  const displayName = series.matchedSeriesName ?? series.sourceSeriesName;
  return (
    <div className="border-border bg-card flex flex-col justify-between gap-2 rounded-lg border p-3 text-xs sm:flex-row sm:items-center">
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-1.5">
          <span className="text-foreground font-semibold break-words">
            {displayName || "Unknown series"}
          </span>
          {series.matchedSeriesId != null && (
            <Badge
              variant="secondary"
              className="gap-1 bg-emerald-500/15 text-[11px] text-emerald-600 dark:text-emerald-400"
            >
              <CheckCircle2 className="h-3 w-3" />
              {series.sourceName}
            </Badge>
          )}
        </div>
        <div className="text-muted-foreground flex flex-wrap items-center gap-x-1.5 text-[11px]">
          <span className="font-medium text-amber-600 dark:text-amber-400">
            {series.missingCount} missing
          </span>
          <span>&middot;</span>
          <span>{series.upcomingCount} upcoming</span>
          <span>&middot;</span>
          <span>{series.ownedBookCount} owned</span>
        </div>
      </div>
      <div className="flex shrink-0 items-center gap-2 self-end sm:self-center">
        {series.matchedSeriesName ? (
          <Link
            to="/library/series/$seriesName"
            params={{ seriesName: series.matchedSeriesName }}
            search={{ authorId }}
            className="text-primary flex items-center hover:underline"
          >
            View series
            <ChevronRight className="ml-1 h-3 w-3" />
          </Link>
        ) : (
          <span
            className="text-muted-foreground flex items-center"
            title="Matching a series to a metadata source happens on the series' own page"
          >
            <Unplug className="mr-1 h-3 w-3" />
            Match series
          </span>
        )}
      </div>
    </div>
  );
}

export default AuthorDetail;
