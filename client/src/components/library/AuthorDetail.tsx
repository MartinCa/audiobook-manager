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
  EyeOff,
  Eye,
  RefreshCw,
  CheckCircle2,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { PAGE_SIZE } from "@/constants/paging";
import { BookListRow } from "./BookListRow";
import { BookBulkActionBar } from "./BookBulkActionBar";
import { SeriesListEntry } from "./SeriesListEntry";
import { AuthorFollowSection } from "./AuthorFollowSection";
import { UpcomingReleasesList } from "./UpcomingReleasesList";
import { LinkButton } from "../LinkButton";
import { CollapsibleCountSection } from "@/components/CollapsibleCountSection";
import { LastRefreshedHint } from "@/components/LastRefreshedHint";
import { formatDate } from "@/helpers/formatHelpers";
import { browseApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { useClampedPage } from "@/hooks/useClampedPage";
import { useBookSelection } from "@/hooks/useBookSelection";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";
import type { AuthorExpectedBook } from "@/types/AuthorDetail";
import { Route } from "@/routes/library/authors/$authorId";

export function AuthorDetail() {
  const { authorId } = Route.useParams();
  const id = Number(authorId);
  const queryClient = useQueryClient();

  // Each section pages server-side; one has its own page state so paging series books doesn't
  // move the standalone list. The unpaged version sent an author's entire catalogue at once.
  const [seriesPage, setSeriesPage] = useState(0);
  const [standalonePage, setStandalonePage] = useState(0);
  const selection = useBookSelection();
  const [refreshing, setRefreshing] = useState(false);
  const [ignoringTitle, setIgnoringTitle] = useState<string | null>(null);

  // Navigating between authors must not carry a previous author's page cursor along. Adjusted
  // during render (React's documented pattern) rather than in an effect: the query key below
  // already changes with the author, so this only resets the local paging state when it does.
  // The book selection resets for the same reason - a different author owns a different roster.
  const [prevId, setPrevId] = useState(id);
  if (prevId !== id) {
    setPrevId(id);
    setSeriesPage(0);
    setStandalonePage(0);
    selection.clear();
  }

  // One combined detail query instead of two: the endpoint already computes both sections on
  // every call and accepts both sections' cursors, so separate queries made every section
  // change issue an extra backend call whose other section (computed with default paging) was
  // thrown away. keepPreviousData keeps both sections rendered while one of them pages.
  const detailQuery = useQuery({
    queryKey: queryKeys.author.detail(id, seriesPage, standalonePage),
    queryFn: () =>
      browseApi.getAuthorDetail(id, {
        seriesLimit: PAGE_SIZE,
        seriesOffset: seriesPage * PAGE_SIZE,
        standaloneLimit: PAGE_SIZE,
        standaloneOffset: standalonePage * PAGE_SIZE,
      }),
    enabled: Boolean(id),
    placeholderData: keepPreviousData,
  });

  const author = detailQuery.data?.author;
  const seriesSection = detailQuery.data?.series ?? { items: [], total: 0 };
  const standaloneSection = detailQuery.data?.standaloneBooks ?? { items: [], total: 0 };
  const missingBooks = detailQuery.data?.missingBooks ?? [];
  const upcomingBooks = detailQuery.data?.upcomingBooks ?? [];
  const ignoredBooks = detailQuery.data?.ignoredBooks ?? [];

  const seriesPageCount = Math.max(1, Math.ceil(seriesSection.total / PAGE_SIZE));
  const standalonePageCount = Math.max(1, Math.ceil(standaloneSection.total / PAGE_SIZE));
  // Clamped so the fetched and the displayed page can never disagree.
  const currentSeriesPage = Math.min(seriesPage, seriesPageCount - 1);
  const currentStandalonePage = Math.min(standalonePage, standalonePageCount - 1);

  // And the raw page states are pulled back into range once a response shows a section shrank
  // under them (e.g. books moved between sections from another tab), so the next fetch - not
  // just the display - lands on a valid page.
  useClampedPage(seriesPage, seriesPageCount, setSeriesPage);
  useClampedPage(standalonePage, standalonePageCount, setStandalonePage);

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
      notifications.success("Refreshed standalone books from source");
      invalidateDetail();
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setRefreshing(false);
    }
  };

  // Mirrors SeriesDetail's ignore/unignore pair: an author's standalone-books roster entry can
  // now be unignored from the Ignored Books section below, so a misclick is recoverable.
  const handleSetIgnored = async (book: AuthorExpectedBook, ignored: boolean) => {
    setIgnoringTitle(book.title);
    try {
      if (ignored) {
        await browseApi.ignoreAuthorExpectedBook(id, book.title);
        notifications.success(`Ignored "${book.title}"`);
      } else {
        await browseApi.unignoreAuthorExpectedBook(id, book.title);
        notifications.success(`Unignored "${book.title}"`);
      }
      invalidateDetail();
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setIgnoringTitle(null);
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

      <div className="space-y-3">
        <h2 className="text-foreground flex items-center gap-2 text-lg font-bold">
          <BookMarked className="text-primary h-5 w-5" />
          Upcoming Releases
        </h2>
        <UpcomingReleasesList
          authorId={author.id}
          emptyMessage="No upcoming releases tracked for this author yet. Follow them to start tracking."
        />
      </div>

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

      {standaloneSection.total > 0 && (
        <div className="space-y-3">
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="text-foreground flex items-center gap-2 text-lg font-bold">
              <BookOpen className="text-primary h-5 w-5" />
              Standalone Audiobooks ({standaloneSection.total})
            </h2>
            <Checkbox
              id="select-standalone-page"
              disabled={standaloneBooks.length === 0}
              checked={standaloneBooks.length > 0 && selection.pageAllSelected(standaloneBooks)}
              indeterminate={
                standaloneBooks.length > 0 && selection.pageSomeSelected(standaloneBooks)
              }
              onCheckedChange={(checked) => {
                if (checked) {
                  selection.selectPage(standaloneBooks);
                } else {
                  selection.deselectPage(standaloneBooks);
                }
              }}
            />
            <label
              htmlFor="select-standalone-page"
              className="text-muted-foreground cursor-pointer text-xs leading-none select-none"
            >
              Select page
            </label>
          </div>
          <div className="space-y-2">
            {standaloneBooks.map((book) => (
              <BookListRow
                key={book.id}
                book={book}
                selectable
                selected={selection.isSelected(book.id)}
                onSelectedChange={() => selection.toggle(book)}
              />
            ))}
          </div>
          <BookBulkActionBar selection={selection} />
          {standalonePageCount > 1 && (
            <div className="flex flex-wrap items-center justify-between gap-2 border-t pt-3">
              <span className="text-muted-foreground text-xs">
                Showing {currentStandalonePage * PAGE_SIZE + 1}–
                {Math.min((currentStandalonePage + 1) * PAGE_SIZE, standaloneSection.total)} of{" "}
                {standaloneSection.total}
              </span>
              <div className="flex items-center gap-2">
                <Button
                  size="sm"
                  variant="outline"
                  disabled={currentStandalonePage === 0}
                  onClick={() => setStandalonePage(currentStandalonePage - 1)}
                >
                  Previous
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  disabled={currentStandalonePage >= standalonePageCount - 1}
                  onClick={() => setStandalonePage(currentStandalonePage + 1)}
                >
                  Next
                </Button>
              </div>
            </div>
          )}
        </div>
      )}

      <CollapsibleCountSection
        label="Missing Books"
        count={missingBooks.length}
        labelClassName="text-amber-600 dark:text-amber-400"
      >
        {missingBooks.length === 0 ? (
          <p className="text-muted-foreground text-xs">
            No missing standalone books detected for this author.
          </p>
        ) : (
          <div className="space-y-2">
            {missingBooks.map((mb) => (
              <AuthorExpectedBookRow
                key={mb.id}
                book={mb}
                tone="amber"
                ignoringTitle={ignoringTitle}
                onIgnore={() => void handleSetIgnored(mb, true)}
              />
            ))}
          </div>
        )}
      </CollapsibleCountSection>

      <CollapsibleCountSection
        label="Upcoming Books"
        count={upcomingBooks.length}
        labelClassName="text-muted-foreground"
      >
        {upcomingBooks.length === 0 ? (
          <p className="text-muted-foreground text-xs">
            No upcoming standalone books detected for this author.
          </p>
        ) : (
          <div className="space-y-2">
            {upcomingBooks.map((ub) => (
              <AuthorExpectedBookRow
                key={ub.id}
                book={ub}
                tone="muted"
                ignoringTitle={ignoringTitle}
                onIgnore={() => void handleSetIgnored(ub, true)}
              />
            ))}
          </div>
        )}
      </CollapsibleCountSection>

      <CollapsibleCountSection
        label="Ignored Books"
        count={ignoredBooks.length}
        labelClassName="text-muted-foreground"
      >
        {ignoredBooks.length === 0 ? (
          <p className="text-muted-foreground text-xs">
            No ignored standalone books for this author.
          </p>
        ) : (
          <div className="space-y-2">
            {ignoredBooks.map((ib) => (
              <div
                key={ib.id}
                className="border-border bg-card flex flex-col justify-between gap-2 rounded-lg border p-3 text-xs opacity-75 sm:flex-row sm:items-center"
              >
                <div className="min-w-0 flex-1">
                  <span className="text-muted-foreground break-words">{ib.title}</span>
                  {ib.year && <span className="text-muted-foreground"> ({ib.year})</span>}
                </div>
                <Button
                  variant="outline"
                  size="sm"
                  className="h-6 self-end text-[11px] sm:self-center"
                  disabled={ignoringTitle === ib.title}
                  onClick={() => {
                    void handleSetIgnored(ib, false);
                  }}
                >
                  {ignoringTitle === ib.title ? (
                    <Loader2 className="mr-1 h-3 w-3 animate-spin" />
                  ) : (
                    <Eye className="mr-1 h-3 w-3" />
                  )}
                  Unignore
                </Button>
              </div>
            ))}
          </div>
        )}
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
                associate this author and enable refreshing their standalone-books roster.
              </p>
            )}
          </div>
        </CardContent>
      </Card>
    </div>
  );
}

/** One row of an author's Missing/Upcoming standalone-books sections - mirrors SeriesDetail's
 * ExpectedBookRow, minus the series-only position field and the "Find in Library" action (the
 * author roster has no per-book candidate-matching endpoint, unlike series' expected-books flow).
 * Ignoring is recoverable via the Ignored Books section's Unignore action below. */
function AuthorExpectedBookRow({
  book,
  tone,
  ignoringTitle,
  onIgnore,
}: {
  book: AuthorExpectedBook;
  tone: "amber" | "muted";
  ignoringTitle: string | null;
  onIgnore: () => void;
}) {
  const toneClasses =
    tone === "amber" ? "border-amber-500/20 bg-amber-500/5" : "border-border bg-card";
  return (
    <div
      className={`flex flex-col justify-between gap-2 rounded-lg border p-3 text-xs sm:flex-row sm:items-center ${toneClasses}`}
    >
      <div className="min-w-0 flex-1">
        <span className="text-foreground font-semibold break-words">{book.title}</span>
        {book.year && <span className="text-muted-foreground"> ({book.year})</span>}
        {book.releaseDate && (
          <span className="text-muted-foreground"> · releases {formatDate(book.releaseDate)}</span>
        )}
      </div>
      <div className="flex shrink-0 items-center gap-2 self-end sm:self-center">
        {book.sourceUrl && (
          <a
            href={book.sourceUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="text-primary flex items-center hover:underline"
          >
            <ExternalLink className="mr-1 h-3 w-3" />
            Source
          </a>
        )}
        <Button
          variant="ghost"
          size="sm"
          className="h-6 text-[11px]"
          disabled={ignoringTitle === book.title}
          onClick={onIgnore}
        >
          {ignoringTitle === book.title ? (
            <Loader2 className="mr-1 h-3 w-3 animate-spin" />
          ) : (
            <EyeOff className="mr-1 h-3 w-3" />
          )}
          Ignore
        </Button>
      </div>
    </div>
  );
}

export default AuthorDetail;
