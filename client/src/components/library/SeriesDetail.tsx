import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useNavigate, useRouter } from "@tanstack/react-router";
import { keepPreviousData, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowLeft,
  ArrowRight,
  RefreshCw,
  Link as LinkIcon,
  ExternalLink,
  Loader2,
  Search,
  Check,
  CheckCircle2,
  Wand2,
  Plus,
  Edit2,
  Trash2,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { PAGE_SIZE } from "@/constants/paging";
import { OperationProgressBar } from "@/components/OperationProgressBar";
import { OperationKeys, SignalREvents } from "@/constants/signalrEvents";
import { useSignalREvent } from "@/hooks/useSignalR";
import { useOperationResync } from "@/hooks/useOperationResync";
import { BookListRow } from "./BookListRow";
import { BookBulkActionBar } from "./BookBulkActionBar";
import { SeriesFollowButton } from "./SeriesFollowButton";
import { UpcomingReleasesList } from "./UpcomingReleasesList";
import { LinkButton } from "../LinkButton";
import { CollapsibleCountSection } from "@/components/CollapsibleCountSection";
import { ExpectedBookList, SectionPager } from "./ExpectedBookList";
import { MissingBookCandidatesDialog } from "./MissingBookCandidatesDialog";
import { BulkMissingBookMatchDialog } from "./BulkMissingBookMatchDialog";
import { SeriesRefreshPendingDialog } from "./SeriesRefreshPendingDialog";
import { LastRefreshedHint } from "@/components/LastRefreshedHint";
import { seriesApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { useClampedPage } from "@/hooks/useClampedPage";
import { useBookSelection } from "@/hooks/useBookSelection";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";
import type { ManagedAudiobook } from "@/types/ManagedAudiobook";
import type {
  SeriesExpectedBook,
  SeriesMatchCandidate,
  SeriesOwnedBook,
  SeriesPartMismatch,
} from "@/types/Series";
import type { SeriesMapping, SeriesMappingBase } from "@/types/SeriesMapping";
import { Route } from "@/routes/library/series/$seriesName";

// SeriesOwnedBookDto omits some summary-row fields BookListRow renders through its
// ManagedAudiobook prop (no series, no genres); fill the gaps with the values the owned row
// actually shows. The cover path is carried through so rows render their cover like the library
// and author views do.
function toManagedBook(b: SeriesOwnedBook): ManagedAudiobook {
  return {
    id: b.id,
    bookName: b.bookName,
    year: b.year,
    seriesPart: b.seriesPart ?? undefined,
    authors: b.authors,
    narrators: b.narrators,
    genres: [],
    durationInSeconds: b.durationInSeconds ?? undefined,
    coverFilePath: b.coverFilePath ?? undefined,
  };
}

/** The matched-to indication shared by the header and the management card: the source name is the link. */
function MatchedSourceLink({
  sourceName,
  sourceUrl,
}: {
  sourceName: string;
  sourceUrl?: string | null;
}) {
  return sourceUrl ? (
    <a
      href={sourceUrl}
      target="_blank"
      rel="noopener noreferrer"
      className="text-primary font-semibold hover:underline"
    >
      {sourceName}
    </a>
  ) : (
    <span className="text-foreground font-semibold">{sourceName}</span>
  );
}

export function SeriesDetail() {
  const { seriesName } = Route.useParams();
  const { authorId } = Route.useSearch();
  const navigate = useNavigate();
  const router = useRouter();
  const queryClient = useQueryClient();
  const selection = useBookSelection();

  // A series change means a whole new roster of owned books; the selection must not carry a
  // previous series' picks across the navigation. Reset during render so a stale selection can
  // never render for the rows of a different series (the component stays mounted across param
  // changes).
  const [prevSeriesName, setPrevSeriesName] = useState(seriesName);
  if (prevSeriesName !== seriesName) {
    setPrevSeriesName(seriesName);
    selection.clear();
  }

  // The "did this series exist" tracker the navigate-away effect below reads. A ref (not state):
  // it exists only for that effect to read, and writing it must happen in an effect, not during
  // render (react-hooks/refs) - unlike the selection reset above, which affects what THIS render
  // shows and so has to be synchronous.
  const hadOverviewRef = useRef(false);
  useEffect(() => {
    hadOverviewRef.current = false;
  }, [seriesName]);

  // Programmatic "leave this series" used when the series stops existing out from under the open
  // page - an explicit delete (SeriesDeleteComplete below), or a bulk edit that moved every owned
  // book of an unmatched series elsewhere (see the seriesDetailQuery effect below). Neither case
  // can be a real link, unlike the visible back control above, which has a stable href.
  const navigateBack = useCallback(() => {
    if (router.history.canGoBack()) {
      router.history.back();
    } else if (authorId) {
      void navigate({
        to: "/library/authors/$authorId",
        params: { authorId: String(authorId) },
      });
    } else {
      void navigate({ to: "/library/series" });
    }
  }, [router, authorId, navigate]);

  const [refreshing, setRefreshing] = useState(false);
  const [loadingCandidates, setLoadingCandidates] = useState(false);
  const [searchingCandidates, setSearchingCandidates] = useState(false);
  const [manualQuery, setManualQuery] = useState("");
  const [candidates, setCandidates] = useState<SeriesMatchCandidate[]>([]);
  const [candidatesLoaded, setCandidatesLoaded] = useState(false);
  const [matchingCandidate, setMatchingCandidate] = useState(false);
  const [updatingOmnibus, setUpdatingOmnibus] = useState(false);
  const [ignoringBookId, setIgnoringBookId] = useState<number | null>(null);
  const [showIgnored, setShowIgnored] = useState(false);

  // Missing book candidates dialog
  const [missingCandidatesOpen, setMissingCandidatesOpen] = useState<
    false | { id: number; position?: string | null; title?: string | null }
  >(false);

  // Bulk missing-book match dialog: one review over every missing book at once.
  const [bulkMatchOpen, setBulkMatchOpen] = useState(false);

  // Pending series-refresh review (the "review changes" banner): a snapshot exists only when a
  // refresh found explicit changes. The detail endpoint 404s when none exists, and that absent
  // case must not surface as an error - the banner renders from `pendingReviews` being defined.
  const [pendingReviewOpen, setPendingReviewOpen] = useState(false);

  // Each section pages server-side: a matched series with a large roster (or a book-heavy
  // series) used to send every owned and expected book over the wire and into the DOM at once.
  // Each section has its own page state so paging one doesn't disturb the others - including the
  // two ignored sub-lists, which page their own classification independently (the review finding:
  // the shared ignored cursor used to advance BOTH sections' pagers at once).
  const [ownedPage, setOwnedPage] = useState(0);
  const [missingPage, setMissingPage] = useState(0);
  const [ignoredMissingPage, setIgnoredMissingPage] = useState(0);
  const [ignoredUpcomingPage, setIgnoredUpcomingPage] = useState(0);
  const [partMismatchPage, setPartMismatchPage] = useState(0);
  const [upcomingPage, setUpcomingPage] = useState(0);
  const [fixingMismatchId, setFixingMismatchId] = useState<number | null>(null);

  // One combined detail query instead of three: the endpoint already computes every section on
  // each call and accepts all the page cursors, so separate queries made every section change
  // issue an extra backend call whose other sections (computed with default paging) were thrown
  // away. keepPreviousData keeps the other sections' items rendered while one section pages.
  const seriesDetailQuery = useQuery({
    queryKey: queryKeys.seriesDetail.detail(
      seriesName,
      authorId,
      ownedPage,
      missingPage,
      ignoredMissingPage,
      partMismatchPage,
      upcomingPage,
      ignoredUpcomingPage,
    ),
    queryFn: () =>
      seriesApi.getSeriesDetail(seriesName, {
        ownedPage,
        ownedPageSize: PAGE_SIZE,
        missingPage,
        missingPageSize: PAGE_SIZE,
        ignoredMissingPage,
        ignoredMissingPageSize: PAGE_SIZE,
        ignoredUpcomingPage,
        ignoredUpcomingPageSize: PAGE_SIZE,
        partMismatchPage,
        partMismatchPageSize: PAGE_SIZE,
        upcomingPage,
        upcomingPageSize: PAGE_SIZE,
      }),
    enabled: Boolean(seriesName),
    placeholderData: keepPreviousData,
  });

  // The regex patterns owned by this series, for the management section's mapping list. They are
  // fetched even for an unmatched series - a pattern may be the very reason it is about to be
  // matched to a name this series owns. The list is already capped server-side at the
  // repository's per-series limit (bounded-list invariant), so whatever arrives here is by
  // construction a bounded set.
  const { data: mappings = [] } = useQuery({
    queryKey: queryKeys.seriesMappings(seriesName),
    queryFn: () => seriesApi.getSeriesMappings(seriesName),
    enabled: Boolean(seriesName),
  });

  const overview = seriesDetailQuery.data?.overview;
  // Remembers, for the navigate-away effect below, that this series was real at some point since
  // the last seriesName change (the reset effect above). A plain ref write, so it never triggers
  // its own re-render.
  useEffect(() => {
    if (overview) {
      hadOverviewRef.current = true;
    }
  }, [overview]);

  // A bulk edit (or any other write) can move every owned book of an UNMATCHED series elsewhere,
  // at which point the backend's GetSeriesDetailPageAsync returns null and this endpoint 404s
  // (SeriesController.GetSeriesDetail) - the series has stopped existing, not just emptied. The
  // invalidation that follows such a write (BookBulkActionBar's BulkEditComplete handler
  // invalidates queryKeys.seriesDetail.all(), and BookDetail's AudiobookSaveComplete does the
  // same for a single-book edit) refetches this query in place; without this effect the page just
  // re-rendered in place on the same now-dead URL. Checked via isError/error rather than
  // "!overview": TanStack Query keeps the LAST successful data cached through a failed background
  // refetch (data does not go back to undefined just because the current fetch attempt errored),
  // so overview alone would never observe the 404. Only navigate away for a series that was
  // actually here before (hadOverviewRef) - a bad URL typed directly, or a series that never
  // existed, must keep showing "Series not found" with its own link back, not bounce the user
  // through history. A MATCHED series with zero owned books is not "gone" (it still has a catalog
  // row and possibly missing/upcoming books) and never 404s, so it never trips this.
  useEffect(() => {
    if (!hadOverviewRef.current || !seriesDetailQuery.isError) {
      return;
    }
    if (handleApiError(seriesDetailQuery.error).status === 404) {
      navigateBack();
    }
  }, [seriesDetailQuery.isError, seriesDetailQuery.error, navigateBack]);

  const ownedSection = seriesDetailQuery.data?.ownedBooks ?? {
    items: [] as SeriesOwnedBook[],
    totalCount: 0,
  };
  const missingSection = seriesDetailQuery.data?.missingBooks ?? {
    items: [] as SeriesExpectedBook[],
    totalCount: 0,
  };
  const ignoredMissingSection = seriesDetailQuery.data?.ignoredMissingBooks ?? {
    items: [] as SeriesExpectedBook[],
    totalCount: 0,
  };
  const ignoredUpcomingSection = seriesDetailQuery.data?.ignoredUpcomingBooks ?? {
    items: [] as SeriesExpectedBook[],
    totalCount: 0,
  };
  const partMismatchSection = seriesDetailQuery.data?.partMismatches ?? {
    items: [] as SeriesPartMismatch[],
    totalCount: 0,
  };
  const upcomingSection = seriesDetailQuery.data?.upcomingBooks ?? {
    items: [] as SeriesExpectedBook[],
    totalCount: 0,
  };
  const ownedPageCount = Math.max(1, Math.ceil(ownedSection.totalCount / PAGE_SIZE));
  const missingPageCount = Math.max(1, Math.ceil(missingSection.totalCount / PAGE_SIZE));
  const ignoredMissingPageCount = Math.max(
    1,
    Math.ceil(ignoredMissingSection.totalCount / PAGE_SIZE),
  );
  const ignoredUpcomingPageCount = Math.max(
    1,
    Math.ceil(ignoredUpcomingSection.totalCount / PAGE_SIZE),
  );
  const partMismatchPageCount = Math.max(1, Math.ceil(partMismatchSection.totalCount / PAGE_SIZE));
  const upcomingPageCount = Math.max(1, Math.ceil(upcomingSection.totalCount / PAGE_SIZE));

  // Clamped here rather than only where the pager is drawn, so the page that is *fetched* and the
  // page that is *displayed* can never disagree (same fix as LibraryConsistency's pager).
  const currentOwnedPage = Math.min(ownedPage, ownedPageCount - 1);
  const currentMissingPage = Math.min(missingPage, missingPageCount - 1);
  const currentIgnoredMissingPage = Math.min(ignoredMissingPage, ignoredMissingPageCount - 1);
  const currentIgnoredUpcomingPage = Math.min(ignoredUpcomingPage, ignoredUpcomingPageCount - 1);
  const currentPartMismatchPage = Math.min(partMismatchPage, partMismatchPageCount - 1);
  const currentUpcomingPage = Math.min(upcomingPage, upcomingPageCount - 1);

  // And the raw page states are corrected back into range once a response shows the total has
  // shrunk under them (e.g. ignoring the last row of the last missing-books page), so the next
  // fetch - not just the display - lands on a valid page.
  useClampedPage(ownedPage, ownedPageCount, setOwnedPage);
  useClampedPage(missingPage, missingPageCount, setMissingPage);
  useClampedPage(ignoredMissingPage, ignoredMissingPageCount, setIgnoredMissingPage);
  useClampedPage(ignoredUpcomingPage, ignoredUpcomingPageCount, setIgnoredUpcomingPage);
  useClampedPage(partMismatchPage, partMismatchPageCount, setPartMismatchPage);
  useClampedPage(upcomingPage, upcomingPageCount, setUpcomingPage);

  // The review banner shares the dialog's query key, so the banner and the open dialog never
  // disagree about whether a snapshot exists. 404 (no snapshot) is the normal absent case and
  // must not surface as an error.
  const { data: pendingReviews } = useQuery({
    queryKey: queryKeys.seriesPending.bySeries(seriesName),
    // 404 (no snapshot) is normalized to undefined by the API layer; map it to null here
    // (queryFn must not resolve undefined) so the banner's "pending &&" stays the absent case.
    queryFn: () => seriesApi.getSeriesPending(seriesName).then((pending) => pending ?? null),
    enabled: Boolean(seriesName) && seriesDetailQuery.data?.overview.isMatched === true,
  });

  const handleRefresh = async () => {
    setRefreshing(true);
    try {
      const result = await seriesApi.refreshSeries(seriesName);
      if (result.hasChanges) {
        notifications.success(
          `Refresh found ${result.changeCount} change${result.changeCount === 1 ? "" : "s"} to review`,
        );
        setPendingReviewOpen(true);
      } else {
        notifications.success("No changes from source");
      }
      void queryClient.invalidateQueries({
        queryKey: queryKeys.seriesDetail.byAuthor(seriesName, authorId),
      });
      void queryClient.invalidateQueries({
        queryKey: queryKeys.seriesPending.bySeries(seriesName),
      });
      void queryClient.invalidateQueries({ queryKey: queryKeys.series.all() });
      void queryClient.invalidateQueries({ queryKey: queryKeys.seriesCounts() });
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setRefreshing(false);
    }
  };

  const handleLoadCandidates = async () => {
    setLoadingCandidates(true);
    try {
      const results = await seriesApi.getMatchCandidates(seriesName);
      setCandidates(results);
      setCandidatesLoaded(true);
      if (results.length === 0) {
        notifications.info("No candidates found automatically. Try searching manually.");
      }
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setLoadingCandidates(false);
    }
  };

  const handleSearchManualCandidates = async () => {
    const q = manualQuery.trim();
    if (!q) return;
    setSearchingCandidates(true);
    try {
      const results = await seriesApi.searchMatchCandidates(seriesName, q);
      setCandidates(results);
      setCandidatesLoaded(true);
      if (results.length === 0) {
        notifications.info("No candidates found for that query.");
      }
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setSearchingCandidates(false);
    }
  };

  const handleApplyMatch = async (candidate: SeriesMatchCandidate) => {
    setMatchingCandidate(true);
    try {
      await seriesApi.matchSeries(
        seriesName,
        candidate.sourceName,
        candidate.sourceId,
        candidate.confidence,
        overview?.includeOmnibusEditions,
      );
      setCandidates([]);
      setCandidatesLoaded(false);
      notifications.success(`Matched to ${candidate.seriesName} (${candidate.sourceName})`);
      void queryClient.invalidateQueries({
        queryKey: queryKeys.seriesDetail.byAuthor(seriesName, authorId),
      });
      void queryClient.invalidateQueries({ queryKey: queryKeys.series.all() });
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setMatchingCandidate(false);
    }
  };

  const handleToggleOmnibus = async (checked: boolean) => {
    setUpdatingOmnibus(true);
    try {
      await seriesApi.setIncludeOmnibusEditions(seriesName, checked);
      notifications.success(checked ? "Omnibus editions included" : "Omnibus editions excluded");
      void queryClient.invalidateQueries({
        queryKey: queryKeys.seriesDetail.byAuthor(seriesName, authorId),
      });
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setUpdatingOmnibus(false);
    }
  };

  const handleSetIgnored = async (
    book: { id: number; position?: string | null; title?: string | null },
    ignored: boolean,
  ) => {
    setIgnoringBookId(book.id);
    try {
      if (ignored) {
        await seriesApi.ignoreExpectedBook(seriesName, book.position, book.title);
        notifications.success(`Ignored "${book.title || "book"}"`);
        // Ignoring moves a book out of the missing or upcoming list (whichever it came from);
        // drop both sections back to page 0 so the refetch below never asks for a page the
        // shrunk section no longer has.
        setMissingPage(0);
        setUpcomingPage(0);
      } else {
        await seriesApi.unignoreExpectedBook(seriesName, book.position, book.title);
        notifications.success(`Unignored "${book.title || "book"}"`);
        // Unignoring moves a book out of the ignored list - but which classification it came from
        // depends on the book's release date, so drop both ignored sections' cursors back to 0
        // (an already-in-range cursor is a no-op for the clamped refetch).
        setIgnoredMissingPage(0);
        setIgnoredUpcomingPage(0);
      }
      void queryClient.invalidateQueries({
        queryKey: queryKeys.seriesDetail.byAuthor(seriesName, authorId),
      });
      // The upcoming-releases view renders the same shared row with the flag applied, so a
      // dismissal here must not keep a stale entry for the query cache's TTL.
      void queryClient.invalidateQueries({ queryKey: queryKeys.upcomingReleases.all() });
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setIgnoringBookId(null);
    }
  };

  // Fixing a part mismatch reuses the expected-books/apply endpoint (the same save-gate-safe
  // UpdateAudiobook pipeline the missing-book flow uses): the mismatch row carries the roster's
  // position and title, which are exactly the natural key the apply endpoint expects. Applying
  // either fixes the part (roster has a position) or clears it, and the apply's own recheck
  // clears the resolved issue.
  const handleFixPartMismatch = async (mismatch: SeriesPartMismatch) => {
    setFixingMismatchId(mismatch.audiobookId);
    try {
      await seriesApi.applyMissingBook(
        seriesName,
        mismatch.audiobookId,
        mismatch.expectedPart,
        mismatch.rosterTitle,
      );
      notifications.success(`Set part ${mismatch.expectedPart} on "${mismatch.bookName}"`);
      // The fix shrinks this list; drop the section back to page 0 so the refetch below never
      // asks for a page the shrunk section no longer has.
      setPartMismatchPage(0);
      void queryClient.invalidateQueries({
        queryKey: queryKeys.seriesDetail.byAuthor(seriesName, authorId),
      });
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setFixingMismatchId(null);
    }
  };

  // --- Series mapping pattern CRUD (the patterns belong to THIS series; the target is always
  // its name, so the dialog carries no target field) ---

  const [mappingDialogOpen, setMappingDialogOpen] = useState(false);
  const [editingMapping, setEditingMapping] = useState<SeriesMapping | null>(null);
  const [mappingRegex, setMappingRegex] = useState("");
  const [mappingWarnAboutPart, setMappingWarnAboutPart] = useState(false);
  const [savingMapping, setSavingMapping] = useState(false);

  const handleOpenCreateMapping = () => {
    setEditingMapping(null);
    setMappingRegex("");
    setMappingWarnAboutPart(false);
    setMappingDialogOpen(true);
  };

  const handleOpenEditMapping = (m: SeriesMapping) => {
    setEditingMapping(m);
    setMappingRegex(m.regex);
    setMappingWarnAboutPart(m.warnAboutPart);
    setMappingDialogOpen(true);
  };

  const handleSaveMapping = async (e: React.FormEvent) => {
    e.preventDefault();
    const regex = mappingRegex.trim();
    if (!regex) return;

    setSavingMapping(true);
    try {
      if (editingMapping) {
        await seriesApi.updateSeriesMapping(seriesName, editingMapping.id, {
          id: editingMapping.id,
          regex,
          warnAboutPart: mappingWarnAboutPart,
        });
        notifications.success("Pattern updated");
      } else {
        const payload: SeriesMappingBase = {
          regex,
          warnAboutPart: mappingWarnAboutPart,
        };
        await seriesApi.createSeriesMapping(seriesName, payload);
        notifications.success("Pattern added");
      }
      setMappingDialogOpen(false);
      void queryClient.invalidateQueries({ queryKey: queryKeys.seriesMappings(seriesName) });
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setSavingMapping(false);
    }
  };

  const handleDeleteMapping = async (id: number) => {
    try {
      await seriesApi.deleteSeriesMapping(seriesName, id);
      notifications.success("Pattern removed");
      void queryClient.invalidateQueries({ queryKey: queryKeys.seriesMappings(seriesName) });
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    }
  };

  // --- Series deletion: clears Series/SeriesPart on every owned book and removes the catalog
  // row (roster, mapping patterns, any pending refresh snapshot). Fire-and-forget, like the other
  // bulk rewrites - progress/completion arrive over SignalR.

  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [deleteProgress, setDeleteProgress] = useState<{
    processed: number;
    total: number;
    succeeded: number;
    failed: number;
  } | null>(null);

  // Recover an in-flight series delete (started elsewhere, or events missed while disconnected)
  // on mount and after a SignalR reconnect: the danger zone must stay disabled and its progress
  // bar must show rather than looking idle, even when the delete confirmation dialog is closed.
  // The returned invalidate is called from the delete's event handlers so a status response
  // fetched before a real event is discarded instead of clobbering the state the event set.
  const invalidateSeriesDelete = useOperationResync(OperationKeys.seriesDelete, (status) => {
    if (status.isRunning) {
      setDeleting(true);
      setDeleteProgress((prev) =>
        prev && prev.total > 0
          ? prev
          : { processed: status.processed, total: status.total, succeeded: 0, failed: 0 },
      );
    } else {
      // A resync discovering a finished delete only unwinds running state that is actually set -
      // it deliberately does NOT reproduce the SeriesDeleteComplete handler's dialog close and
      // navigation. The series may already be gone: the detail refetch then renders its Not
      // Found state and the user navigates from there, exactly like a page that never saw the
      // in-flight delete at all. The delete confirmation dialog state is untouched either way.
      setDeleting((prev) => (prev ? false : prev));
      setDeleteProgress((prev) => (prev ? null : prev));
    }
  });

  useSignalREvent<{ processed: number; total: number; succeeded: number; failed: number }>(
    SignalREvents.SeriesDeleteProgress,
    (data) => {
      invalidateSeriesDelete();
      setDeleting(true);
      setDeleteProgress(data);
    },
  );

  useSignalREvent<{
    totalProcessed: number;
    totalSucceeded: number;
    totalFailed: number;
    errored: boolean;
  }>(SignalREvents.SeriesDeleteComplete, (data) => {
    invalidateSeriesDelete();
    setDeleting(false);
    setDeleteProgress(null);
    void queryClient.invalidateQueries({ queryKey: ["seriesDetail", seriesName, authorId] });

    // errored means the delete threw out of the background operation (e.g. the catalog row
    // delete itself failed) - every count here is zero, indistinguishable from "a series with no
    // owned books, deleted successfully" without this flag. Leave the dialog open on the current
    // (possibly partially-cleared) series rather than reporting success and navigating away.
    if (data.errored) {
      notifications.error("Series deletion failed");
      return;
    }

    setDeleteDialogOpen(false);
    if (data.totalFailed > 0) {
      notifications.error(
        `Series deleted with ${data.totalFailed} book${data.totalFailed === 1 ? "" : "s"} that could not be cleared`,
      );
    } else {
      notifications.success("Series deleted");
    }
    void queryClient.invalidateQueries({ queryKey: ["series"] });
    void queryClient.invalidateQueries({ queryKey: ["seriesCounts"] });
    navigateBack();
  });

  const handleDeleteSeries = async () => {
    setDeleting(true);
    setDeleteProgress(null);
    try {
      await seriesApi.startDeleteSeries(seriesName);
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
      setDeleting(false);
    }
  };

  if (!overview && seriesDetailQuery.isLoading) {
    return (
      <div className="text-muted-foreground flex flex-col items-center justify-center py-20">
        <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
        <p className="text-sm">Loading series details...</p>
      </div>
    );
  }

  if (!overview) {
    return (
      <div className="space-y-4 py-12 text-center">
        <h2 className="text-xl font-bold">Series not found</h2>
        <LinkButton render={<Link to="/library/series" />}>Back to Series</LinkButton>
      </div>
    );
  }

  const ownedBooks = ownedSection.items as SeriesOwnedBook[];
  const ownedManagedBooks = ownedBooks.map(toManagedBook);
  const missingBooks = missingSection.items as SeriesExpectedBook[];
  const ignoredMissingBooks = ignoredMissingSection.items as SeriesExpectedBook[];
  const ignoredUpcomingBooks = ignoredUpcomingSection.items as SeriesExpectedBook[];
  const partMismatchBooks = partMismatchSection.items as SeriesPartMismatch[];
  const upcomingBooks = upcomingSection.items as SeriesExpectedBook[];

  // The two classification lists partition the scope's ignored rows, so their totals sum to the
  // combined count the "show ignored" toggle label describes.
  const ignoredTotal = ignoredMissingSection.totalCount + ignoredUpcomingSection.totalCount;

  return (
    <div className="space-y-6">
      <div>
        <LinkButton
          variant="ghost"
          size="sm"
          render={
            authorId ? (
              <Link to="/library/authors/$authorId" params={{ authorId: String(authorId) }} />
            ) : (
              <Link to="/library/series" />
            )
          }
          className="w-full justify-start sm:w-auto"
        >
          <ArrowLeft className="mr-2 h-4 w-4" />
          {authorId ? "Back to Author" : "Back to Series"}
        </LinkButton>
      </div>

      <div className="border-border border-b pb-4">
        <h1 className="text-foreground text-2xl font-bold break-words">{seriesName}</h1>
        <div className="text-muted-foreground flex flex-wrap items-center gap-x-2 gap-y-1 text-sm">
          <span>
            {overview.ownedBookCount} {overview.ownedBookCount === 1 ? "book" : "books"} owned
          </span>
          {overview.isMatched && (
            <span
              className={
                overview.missingBookCount > 0
                  ? "font-semibold text-amber-600 dark:text-amber-400"
                  : undefined
              }
            >
              &middot; {overview.missingBookCount} missing
            </span>
          )}
          {overview.isMatched && overview.matchedSourceName && (
            <span>
              &middot; Matched to{" "}
              <MatchedSourceLink
                sourceName={overview.matchedSourceName}
                sourceUrl={overview.matchedSourceUrl}
              />
            </span>
          )}
        </div>
        <div className="mt-3">
          <SeriesFollowButton
            seriesName={seriesName}
            onChanged={() => {
              void queryClient.invalidateQueries({
                queryKey: queryKeys.seriesDetail.byAuthor(seriesName, authorId),
              });
            }}
          />
        </div>
      </div>

      <div className="space-y-3">
        <h2 className="text-foreground text-lg font-bold">Upcoming Releases</h2>
        {overview.id != null ? (
          <UpcomingReleasesList
            seriesId={overview.id}
            emptyMessage="No upcoming releases tracked for this series yet. Follow it to start tracking."
          />
        ) : (
          <p className="text-muted-foreground py-4 text-center text-sm">
            Follow this series to start tracking its upcoming releases.
          </p>
        )}
      </div>

      <div className="space-y-4">
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="text-foreground text-lg font-bold">
            Owned Books ({ownedSection.totalCount})
          </h2>
          <Checkbox
            id="select-owned-page"
            disabled={ownedManagedBooks.length === 0}
            checked={ownedManagedBooks.length > 0 && selection.pageAllSelected(ownedManagedBooks)}
            indeterminate={
              ownedManagedBooks.length > 0 && selection.pageSomeSelected(ownedManagedBooks)
            }
            onCheckedChange={(checked) => {
              if (checked) {
                selection.selectPage(ownedManagedBooks);
              } else {
                selection.deselectPage(ownedManagedBooks);
              }
            }}
          />
          <label
            htmlFor="select-owned-page"
            className="text-muted-foreground cursor-pointer text-xs leading-none select-none"
          >
            Select page
          </label>
        </div>
        {ownedManagedBooks.length === 0 ? (
          <p className="text-muted-foreground text-sm">No books owned.</p>
        ) : (
          <div className="space-y-2">
            {ownedManagedBooks.map((b) => (
              <BookListRow
                key={b.id}
                book={b}
                showSeriesPart
                hideSeries
                selectable
                selected={selection.isSelected(b.id)}
                onSelectedChange={() => selection.toggle(b)}
              />
            ))}
          </div>
        )}
        <BookBulkActionBar selection={selection} />
        {ownedPageCount > 1 && (
          <SectionPager
            currentPage={currentOwnedPage}
            pageCount={ownedPageCount}
            totalCount={ownedSection.totalCount}
            onPageChange={setOwnedPage}
          />
        )}
      </div>

      {overview.isMatched && ignoredTotal > 0 && (
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
            Show ignored books ({ignoredTotal})
          </label>
        </div>
      )}

      {overview.isMatched && (
        <CollapsibleCountSection
          label="Missing Books"
          count={missingSection.totalCount}
          labelClassName="text-amber-600 dark:text-amber-400"
        >
          {missingSection.totalCount > 0 && (
            <Button
              variant="secondary"
              size="sm"
              className="w-full sm:w-auto"
              onClick={() => setBulkMatchOpen(true)}
            >
              <Wand2 className="mr-1 h-3 w-3" />
              Match Missing Books
            </Button>
          )}
          <ExpectedBookList
            section="missing"
            items={missingBooks}
            ignoredItems={ignoredMissingBooks}
            // The server pre-splits and pages each ignored classification with its own UTC clock;
            // re-classifying the rows here with the local one dropped boundary rows the pager
            // still counted.
            ignoredItemsPreClassified
            ignoredTotal={ignoredMissingSection.totalCount}
            ignoredPager={{
              currentPage: currentIgnoredMissingPage,
              pageCount: ignoredMissingPageCount,
              onPageChange: setIgnoredMissingPage,
            }}
            showIgnored={showIgnored}
            busyBookId={ignoringBookId}
            emptyMessage="No missing books detected in this series."
            onIgnore={(book) => void handleSetIgnored(book, true)}
            onUnignore={(book) => void handleSetIgnored(book, false)}
            onFindInLibrary={(book) =>
              setMissingCandidatesOpen({ id: book.id, position: book.position, title: book.title })
            }
          />
          {missingPageCount > 1 && (
            <SectionPager
              currentPage={currentMissingPage}
              pageCount={missingPageCount}
              totalCount={missingSection.totalCount}
              onPageChange={setMissingPage}
            />
          )}
        </CollapsibleCountSection>
      )}

      {overview.isMatched && (
        <CollapsibleCountSection
          label="Upcoming Books"
          count={upcomingSection.totalCount}
          labelClassName="text-muted-foreground"
        >
          <ExpectedBookList
            section="upcoming"
            items={upcomingBooks}
            ignoredItems={ignoredUpcomingBooks}
            ignoredItemsPreClassified
            ignoredTotal={ignoredUpcomingSection.totalCount}
            ignoredPager={{
              currentPage: currentIgnoredUpcomingPage,
              pageCount: ignoredUpcomingPageCount,
              onPageChange: setIgnoredUpcomingPage,
            }}
            showIgnored={showIgnored}
            busyBookId={ignoringBookId}
            emptyMessage="No upcoming books detected in this series."
            onIgnore={(book) => void handleSetIgnored(book, true)}
            onUnignore={(book) => void handleSetIgnored(book, false)}
          />
          {upcomingPageCount > 1 && (
            <SectionPager
              currentPage={currentUpcomingPage}
              pageCount={upcomingPageCount}
              totalCount={upcomingSection.totalCount}
              onPageChange={setUpcomingPage}
            />
          )}
        </CollapsibleCountSection>
      )}

      {overview.isMatched && partMismatchSection.totalCount > 0 && (
        <div className="space-y-4">
          <h2 className="text-lg font-bold text-orange-600 dark:text-orange-400">
            Part Mismatches ({partMismatchSection.totalCount})
          </h2>
          <p className="text-muted-foreground w-2/3 text-xs">
            These owned books carry no series part, or one that differs from the position this
            matched series assigns them.
          </p>
          <div className="space-y-2">
            {partMismatchBooks.map((pm) => (
              <div
                key={pm.audiobookId}
                className="flex flex-col justify-between gap-2 rounded-lg border border-orange-500/20 bg-orange-500/5 p-3 text-xs sm:flex-row sm:items-center"
              >
                <div className="min-w-0 flex-1">
                  <span className="text-foreground font-semibold break-words">{pm.bookName}</span>
                  <div className="text-muted-foreground mt-0.5 break-words">
                    stored part <span className="line-through">{pm.storedPart ?? "—"}</span>{" "}
                    <ArrowRight className="inline h-3 w-3" /> part {pm.expectedPart} (shared with "
                    {pm.rosterTitle}")
                  </div>
                </div>
                <Button
                  variant="secondary"
                  size="sm"
                  className="h-6 self-end text-[11px] sm:self-center"
                  disabled={fixingMismatchId === pm.audiobookId}
                  onClick={() => {
                    void handleFixPartMismatch(pm);
                  }}
                >
                  {fixingMismatchId === pm.audiobookId ? (
                    <Loader2 className="mr-1 h-3 w-3 animate-spin" />
                  ) : (
                    <Check className="mr-1 h-3 w-3" />
                  )}
                  Fix
                </Button>
              </div>
            ))}
          </div>
          {partMismatchPageCount > 1 && (
            <SectionPager
              currentPage={currentPartMismatchPage}
              pageCount={partMismatchPageCount}
              totalCount={partMismatchSection.totalCount}
              onPageChange={setPartMismatchPage}
            />
          )}
        </div>
      )}

      {/* Management & Settings: match/refresh actions, the metadata provider details, and this
          series' mapping patterns. Moved here from the page header and the global Settings page
          so the series' critical info and books stay on top and its settings travel with it. */}
      <Card>
        <CardHeader className="py-3">
          <CardTitle className="text-muted-foreground text-sm font-semibold uppercase">
            Management &amp; Settings
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-4 text-xs">
          <div className="flex flex-wrap items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              className="w-full sm:w-auto"
              disabled={loadingCandidates || matchingCandidate}
              onClick={() => {
                void handleLoadCandidates();
              }}
            >
              {loadingCandidates ? (
                <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />
              ) : (
                <LinkIcon className="mr-1.5 h-4 w-4" />
              )}
              {overview.isMatched ? "Re-match to Source" : "Match to Source"}
            </Button>

            {overview.isMatched && (
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
            )}
          </div>

          {/* Metadata Provider */}
          <div className="border-border space-y-4 border-t pt-3">
            <div className="flex flex-col gap-3 sm:flex-row sm:flex-wrap sm:items-center">
              {/* Same guard as the header: a matched series without a source name is not a
                  nameable match, so it renders as the incomplete state instead of a dangling
                  "Matched to " label. The source name itself is the link to the source. */}
              {overview.isMatched && overview.matchedSourceName ? (
                <>
                  <Badge
                    variant="secondary"
                    className="bg-emerald-500/15 text-emerald-600 dark:text-emerald-400"
                  >
                    <CheckCircle2 className="mr-1 h-3.5 w-3.5" />
                    Matched to{" "}
                    <MatchedSourceLink
                      sourceName={overview.matchedSourceName}
                      sourceUrl={overview.matchedSourceUrl}
                    />
                  </Badge>
                  {overview.matchConfidence != null && (
                    <span className="text-muted-foreground">
                      Confidence: {Math.round(overview.matchConfidence * 100)}%
                    </span>
                  )}
                  <span className="text-muted-foreground">
                    <LastRefreshedHint lastRefreshedAt={overview.lastRefreshedAt} />
                  </span>
                </>
              ) : (
                <p className="text-muted-foreground">
                  Not matched to an online metadata provider yet. Click "Match to Source" or search
                  below to associate this series.
                </p>
              )}
            </div>

            {pendingReviews && (
              <div className="border-border flex flex-col justify-between gap-2 rounded-md border border-amber-500/30 bg-amber-500/5 p-3 sm:flex-row sm:items-center">
                <div className="text-xs">
                  {pendingReviews.changes.length === 0 ? (
                    <>
                      <span className="text-foreground font-semibold">
                        Series name alignment pending
                      </span>
                      <span className="text-muted-foreground">
                        {" "}
                        from the last refresh. Review it before it is written to your books.
                      </span>
                    </>
                  ) : (
                    <>
                      <span className="text-foreground font-semibold">
                        {pendingReviews.changes.length} pending change
                        {pendingReviews.changes.length === 1 ? "" : "s"}
                      </span>
                      <span className="text-muted-foreground">
                        {" "}
                        from the last refresh. Review them before they are written to your books.
                      </span>
                    </>
                  )}
                </div>
                <Button
                  size="sm"
                  className="h-7 shrink-0 self-end text-xs sm:self-center"
                  onClick={() => setPendingReviewOpen(true)}
                >
                  Review Changes
                </Button>
              </div>
            )}

            <div className="flex items-center space-x-2 pt-1">
              <Checkbox
                id="includeOmnibus"
                checked={overview.includeOmnibusEditions}
                disabled={updatingOmnibus}
                onCheckedChange={(checked) => {
                  void handleToggleOmnibus(Boolean(checked));
                }}
              />
              <label
                htmlFor="includeOmnibus"
                className="text-muted-foreground cursor-pointer text-xs leading-none select-none"
              >
                Include omnibus/box-set editions in missing books list
              </label>
            </div>

            <div className="border-border space-y-2 border-t pt-3">
              <label className="text-muted-foreground font-semibold">
                Search title/author or paste a series URL
              </label>
              <div className="flex max-w-xl flex-col gap-2 sm:flex-row">
                <Input
                  placeholder="e.g. Harry Potter, or https://hardcover.app/series/..."
                  value={manualQuery}
                  onChange={(e) => setManualQuery(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") {
                      e.preventDefault();
                      void handleSearchManualCandidates();
                    }
                  }}
                  className="h-8 flex-1 text-xs"
                />
                <Button
                  variant="secondary"
                  size="sm"
                  className="h-8 w-full text-xs sm:w-auto"
                  disabled={searchingCandidates || !manualQuery.trim()}
                  onClick={() => {
                    void handleSearchManualCandidates();
                  }}
                >
                  {searchingCandidates ? (
                    <Loader2 className="mr-1 h-3.5 w-3.5 animate-spin" />
                  ) : (
                    <Search className="mr-1 h-3.5 w-3.5" />
                  )}
                  Search
                </Button>
              </div>
            </div>

            {candidatesLoaded && (
              <div className="border-border space-y-2 border-t pt-3">
                <div className="flex items-center justify-between">
                  <span className="font-semibold">Match Candidates ({candidates.length})</span>
                  <Button
                    variant="ghost"
                    size="sm"
                    className="h-6 text-[11px]"
                    onClick={() => {
                      setCandidates([]);
                      setCandidatesLoaded(false);
                    }}
                  >
                    Clear Candidates
                  </Button>
                </div>

                {candidates.length === 0 ? (
                  <p className="text-muted-foreground py-2 italic">
                    No candidates found for that search.
                  </p>
                ) : (
                  <div className="space-y-2">
                    {candidates.map((c) => (
                      <div
                        key={`${c.sourceName}-${c.sourceId}`}
                        className="border-border bg-muted/30 flex flex-col justify-between gap-3 rounded-md border p-2.5 sm:flex-row sm:items-center"
                      >
                        <div className="min-w-0 flex-1">
                          <div className="flex flex-wrap items-center gap-1.5 sm:gap-2">
                            <span className="font-semibold break-words">{c.seriesName}</span>
                            <Badge variant="outline" className="text-[10px]">
                              {c.sourceName}
                            </Badge>
                            <span className="text-muted-foreground text-[11px]">
                              {Math.round(c.confidence * 100)}% match
                            </span>
                          </div>
                          <div className="text-muted-foreground text-[11px] break-words">
                            {c.authors.join(", ") || "Unknown author"}
                            {c.bookCount != null ? ` · ${c.bookCount} books` : ""}
                          </div>
                        </div>
                        <div className="flex shrink-0 items-center gap-2 self-end sm:self-center">
                          {c.sourceUrl && (
                            <a
                              href={c.sourceUrl}
                              target="_blank"
                              rel="noopener noreferrer"
                              className="text-primary hover:underline"
                            >
                              <ExternalLink className="h-3.5 w-3.5" />
                            </a>
                          )}
                          <Button
                            size="sm"
                            className="h-7 text-xs"
                            disabled={matchingCandidate}
                            onClick={() => {
                              void handleApplyMatch(c);
                            }}
                          >
                            {matchingCandidate ? (
                              <Loader2 className="mr-1 h-3 w-3 animate-spin" />
                            ) : (
                              <Check className="mr-1 h-3 w-3" />
                            )}
                            Apply Match
                          </Button>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            )}
          </div>

          {/* Mapping patterns owned by this series */}
          <div className="border-border space-y-3 border-t pt-4">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="space-y-0.5">
                <span className="text-foreground font-semibold">
                  Series Mapping Patterns ({mappings.length})
                </span>
                <p className="text-muted-foreground max-w-xl">
                  Regex patterns that route scraped or embedded series values to this series. A
                  matching value is rewritten to "{seriesName}".
                </p>
              </div>
              <Button
                variant="outline"
                size="sm"
                className="h-7 text-xs"
                onClick={handleOpenCreateMapping}
              >
                <Plus className="mr-1 h-3 w-3" />
                Add Pattern
              </Button>
            </div>

            {mappings.length === 0 ? (
              <p className="text-muted-foreground border-border rounded-md border border-dashed p-4 text-center">
                No mapping patterns yet. Add one to normalize incoming series values to this series.
              </p>
            ) : (
              <div className="space-y-1.5">
                {mappings.map((m) => (
                  <div
                    key={m.id}
                    className="border-border bg-muted/40 flex flex-col justify-between gap-2 rounded px-3 py-2 sm:flex-row sm:items-center"
                  >
                    <div className="min-w-0 flex-1">
                      <span className="font-mono text-[11px] break-all">{m.regex}</span>
                      {m.warnAboutPart && (
                        <Badge variant="outline" className="text-muted-foreground ml-2 text-[10px]">
                          warn on part
                        </Badge>
                      )}
                    </div>
                    <div className="flex shrink-0 items-center gap-1 self-end sm:self-center">
                      <Button
                        variant="ghost"
                        size="icon"
                        className="h-7 w-7"
                        aria-label={`Edit pattern ${m.id}`}
                        onClick={() => handleOpenEditMapping(m)}
                      >
                        <Edit2 className="h-3.5 w-3.5" />
                      </Button>
                      <Button
                        variant="ghost"
                        size="icon"
                        className="text-destructive h-7 w-7"
                        aria-label={`Delete pattern ${m.id}`}
                        onClick={() => {
                          void handleDeleteMapping(m.id);
                        }}
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </Button>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* Danger zone: deleting the series clears Series/SeriesPart on every owned book and
              removes the catalog row (roster, mapping patterns, any pending refresh snapshot). */}
          <div className="border-destructive/30 space-y-2 border-t pt-4">
            <span className="text-destructive font-semibold">Danger Zone</span>
            <p className="text-muted-foreground max-w-xl">
              Deleting this series clears the Series and Series Part fields on every owned book, and
              removes the series' matching, roster and mapping data. The books keep their content
              and are not deleted, but clearing these fields moves them out of the series folder and
              renames their files.
            </p>
            <Button
              variant="destructive"
              size="sm"
              className="h-7 text-xs"
              onClick={() => setDeleteDialogOpen(true)}
            >
              <Trash2 className="mr-1 h-3 w-3" />
              Delete Series
            </Button>
          </div>
        </CardContent>
      </Card>

      <MissingBookCandidatesDialog
        open={typeof missingCandidatesOpen === "object"}
        onOpenChange={(open) => {
          setMissingCandidatesOpen(open ? missingCandidatesOpen : false);
        }}
        seriesName={seriesName}
        missingBook={
          typeof missingCandidatesOpen === "object"
            ? {
                id: missingCandidatesOpen.id,
                position: missingCandidatesOpen.position,
                title: missingCandidatesOpen.title,
              }
            : {
                id: 0,
                position: null,
                title: null,
              }
        }
      />

      <BulkMissingBookMatchDialog
        open={bulkMatchOpen}
        onOpenChange={setBulkMatchOpen}
        seriesName={seriesName}
      />

      <SeriesRefreshPendingDialog
        open={pendingReviewOpen}
        onOpenChange={setPendingReviewOpen}
        seriesName={seriesName}
        onApplied={(renamedTo) => {
          if (renamedTo && renamedTo !== seriesName) {
            // The apply fully adopted the source's series name: the current route's detail
            // query now 404s (nobody owns the old name anymore). Move to the adopted name with
            // replace so the back-stack still points where the user came from, and invalidate
            // the new name's views so nothing stale is served for it.
            void navigate({
              to: "/library/series/$seriesName",
              params: { seriesName: renamedTo },
              search: { authorId },
              replace: true,
            });
            void queryClient.invalidateQueries({ queryKey: queryKeys.seriesDetail.all() });
            void queryClient.invalidateQueries({
              queryKey: queryKeys.seriesPending.bySeries(renamedTo),
            });
            void queryClient.invalidateQueries({ queryKey: queryKeys.seriesMappings(renamedTo) });
          } else {
            void queryClient.invalidateQueries({
              queryKey: queryKeys.seriesDetail.byAuthor(seriesName, authorId),
            });
          }
        }}
      />

      <Dialog open={mappingDialogOpen} onOpenChange={setMappingDialogOpen}>
        <DialogContent className="w-[calc(100vw-2rem)] p-4 sm:max-w-md sm:p-6">
          <DialogHeader>
            <DialogTitle>
              {editingMapping ? "Edit Mapping Pattern" : "Add Mapping Pattern"}
            </DialogTitle>
          </DialogHeader>

          <form
            onSubmit={(e) => {
              void handleSaveMapping(e);
            }}
            className="space-y-4 py-2"
          >
            <p className="text-muted-foreground text-xs">
              Values matching this regex are normalized to this series: "{seriesName}".
            </p>

            <div className="space-y-1">
              <label
                htmlFor="mappingRegex"
                className="text-muted-foreground text-xs font-semibold uppercase"
              >
                Regex Pattern <span className="text-destructive">*</span>
              </label>
              <Input
                id="mappingRegex"
                placeholder="(?i)^wheel of time.*"
                value={mappingRegex}
                onChange={(e) => setMappingRegex(e.target.value)}
                className="font-mono"
                required
              />
            </div>

            <div className="flex items-center space-x-2 pt-1">
              <input
                type="checkbox"
                id="mappingWarnAboutPart"
                checked={mappingWarnAboutPart}
                onChange={(e) => setMappingWarnAboutPart(e.target.checked)}
                className="border-border h-4 w-4 rounded"
              />
              <label
                htmlFor="mappingWarnAboutPart"
                className="text-muted-foreground cursor-pointer text-xs"
              >
                Warn if series part is found
              </label>
            </div>

            <div className="border-border flex flex-col-reverse justify-end gap-2 border-t pt-4 sm:flex-row">
              <Button
                type="button"
                variant="outline"
                className="w-full sm:w-auto"
                onClick={() => setMappingDialogOpen(false)}
              >
                Cancel
              </Button>
              <Button type="submit" className="w-full sm:w-auto" disabled={savingMapping}>
                {savingMapping ? <Loader2 className="mr-1.5 h-4 w-4 animate-spin" /> : null}
                {editingMapping ? "Save Changes" : "Add Pattern"}
              </Button>
            </div>
          </form>
        </DialogContent>
      </Dialog>

      <Dialog
        open={deleteDialogOpen}
        onOpenChange={(open) => {
          if (!deleting) setDeleteDialogOpen(open);
        }}
      >
        <DialogContent className="w-[calc(100vw-2rem)] p-4 sm:max-w-lg sm:p-6">
          <DialogHeader>
            <DialogTitle>Delete "{seriesName}"?</DialogTitle>
          </DialogHeader>

          <div className="space-y-3 py-2 text-sm">
            <p>
              This clears the following fields on{" "}
              <span className="text-foreground font-semibold">
                {ownedSection.totalCount} owned book{ownedSection.totalCount === 1 ? "" : "s"}
              </span>
              :
            </p>
            <ul className="text-muted-foreground list-disc space-y-0.5 pl-5">
              <li>
                <span className="text-foreground font-medium">Series</span> — cleared
              </li>
              <li>
                <span className="text-foreground font-medium">Series Part</span> — cleared
              </li>
            </ul>
            {ownedBooks.length > 0 && (
              <div className="border-border bg-muted/30 max-h-40 overflow-y-auto rounded-md border p-2">
                <ul className="text-muted-foreground space-y-0.5 text-xs">
                  {ownedBooks.map((b) => (
                    <li key={b.id} className="truncate">
                      {b.seriesPart ? `Part ${b.seriesPart} — ` : ""}
                      {b.bookName}
                    </li>
                  ))}
                </ul>
                {ownedSection.totalCount > ownedBooks.length && (
                  <p className="text-muted-foreground mt-1 text-xs italic">
                    and {ownedSection.totalCount - ownedBooks.length} more...
                  </p>
                )}
              </div>
            )}
            <p className="text-muted-foreground">
              The series' matching, roster and mapping data is removed. The books keep their content
              and are not deleted, but clearing these fields moves them out of the series folder and
              renames their files. This cannot be undone.
            </p>
          </div>

          {deleting && (
            <OperationProgressBar
              processed={deleteProgress?.processed ?? 0}
              total={deleteProgress?.total ?? 0}
              label={
                deleteProgress
                  ? `Clearing books (${deleteProgress.succeeded} succeeded, ${deleteProgress.failed} failed)`
                  : "Starting..."
              }
            />
          )}

          <div className="border-border flex flex-col-reverse justify-end gap-2 border-t pt-4 sm:flex-row">
            <Button
              type="button"
              variant="outline"
              className="w-full sm:w-auto"
              disabled={deleting}
              onClick={() => setDeleteDialogOpen(false)}
            >
              Cancel
            </Button>
            <Button
              type="button"
              variant="destructive"
              className="w-full sm:w-auto"
              disabled={deleting}
              onClick={() => {
                void handleDeleteSeries();
              }}
            >
              {deleting ? <Loader2 className="mr-1.5 h-4 w-4 animate-spin" /> : null}
              Delete Series
            </Button>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
}

export default SeriesDetail;
