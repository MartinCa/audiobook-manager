import { useEffect, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { BellRing, Bell, Link2, Link2Off, Loader2, Search, AlertCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { browseApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";
import type { AuthorMatchCandidate } from "@/types/UpcomingRelease";

interface AuthorFollowSectionProps {
  authorId: number;
  authorName: string;
}

/**
 * Follow/unfollow plus the Hardcover author match - the two things the upcoming-releases worker
 * needs before it can poll this author. Following without a match is allowed (the worker simply
 * skips an unmatched followed author), but the UI nudges toward matching first since an
 * unmatched follow never produces anything.
 */
export function AuthorFollowSection({ authorId, authorName }: AuthorFollowSectionProps) {
  const queryClient = useQueryClient();
  const [matchDialogOpen, setMatchDialogOpen] = useState(false);
  const [busy, setBusy] = useState(false);

  const followQuery = useQuery({
    queryKey: queryKeys.authorFollow(authorId),
    queryFn: () => browseApi.getAuthorFollowStatus(authorId),
  });

  const matchQuery = useQuery({
    queryKey: queryKeys.authorHardcoverMatch(authorId),
    queryFn: () => browseApi.getAuthorHardcoverMatch(authorId),
  });

  const isFollowed = followQuery.data?.isFollowed ?? false;
  const isMatched = Boolean(matchQuery.data?.sourceId);

  const handleToggleFollow = async () => {
    setBusy(true);
    try {
      if (isFollowed) {
        await browseApi.unfollowAuthor(authorId);
      } else {
        await browseApi.followAuthor(authorId);
      }
      await queryClient.invalidateQueries({ queryKey: queryKeys.authorFollow(authorId) });
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setBusy(false);
    }
  };

  const handleUnmatch = async () => {
    setBusy(true);
    try {
      await browseApi.unmatchAuthorFromHardcover(authorId);
      await queryClient.invalidateQueries({ queryKey: queryKeys.authorHardcoverMatch(authorId) });
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex flex-wrap items-center gap-2">
      <Button
        variant={isFollowed ? "default" : "outline"}
        size="sm"
        disabled={busy || followQuery.isLoading}
        onClick={() => {
          void handleToggleFollow();
        }}
      >
        {isFollowed ? (
          <BellRing className="mr-1.5 h-3.5 w-3.5" />
        ) : (
          <Bell className="mr-1.5 h-3.5 w-3.5" />
        )}
        {isFollowed ? "Following" : "Follow for upcoming releases"}
      </Button>

      {isMatched ? (
        <Button variant="ghost" size="sm" disabled={busy} onClick={() => void handleUnmatch()}>
          <Link2Off className="mr-1.5 h-3.5 w-3.5" />
          Unlink Hardcover match
        </Button>
      ) : (
        <Button variant="ghost" size="sm" onClick={() => setMatchDialogOpen(true)}>
          <Link2 className="mr-1.5 h-3.5 w-3.5" />
          Match to Hardcover
        </Button>
      )}

      <AuthorMatchDialog
        authorId={authorId}
        authorName={authorName}
        open={matchDialogOpen}
        onOpenChange={setMatchDialogOpen}
      />
    </div>
  );
}

interface AuthorMatchDialogProps {
  authorId: number;
  authorName: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

// Below this, a search is not worth firing - matches the entry-time type-ahead's own floor
// (see similarValuesApi.getAutocomplete callers) and avoids a burst of single/double-letter
// requests against the shared Hardcover daily budget while the user is still typing a name.
const MIN_SEARCH_LENGTH = 2;

function AuthorMatchDialog({ authorId, authorName, open, onOpenChange }: AuthorMatchDialogProps) {
  const queryClient = useQueryClient();
  const [query, setQuery] = useState(authorName);
  const [debouncedQuery, setDebouncedQuery] = useState(authorName);
  const [matching, setMatching] = useState(false);
  // Which candidate is being applied - the match call runs a roster refresh server-side and can
  // take seconds, so the clicked candidate keeps a spinner while every other row is disabled.
  const [matchingSourceId, setMatchingSourceId] = useState<string | null>(null);

  // Reset the local search/result state whenever the dialog opens, or whenever it is asked to
  // match a different author while already open. AuthorFollowSection is rendered once per
  // author-detail page with no `key`, and TanStack Router does not remount the tree on a
  // param-only navigation, so this dialog instance is reused across authors - without this reset,
  // `useState(authorName)` above only seeds `query`/`debouncedQuery` on the very first mount and
  // every later author reopens the dialog with the previous author's stale search text. Mirrors
  // SeriesMatchDialog's prevOpen pattern.
  const [prevOpen, setPrevOpen] = useState(open);
  const [prevAuthorId, setPrevAuthorId] = useState(authorId);
  if (open !== prevOpen || authorId !== prevAuthorId) {
    setPrevOpen(open);
    setPrevAuthorId(authorId);
    if (open) {
      setQuery(authorName);
      setDebouncedQuery(authorName);
      setMatching(false);
      setMatchingSourceId(null);
    }
  }

  // Debounced like AuthorsList's own filter: a fast typist must not enqueue a Hardcover search
  // request per keystroke through the shared 5000/day budget.
  useEffect(() => {
    const timer = setTimeout(() => setDebouncedQuery(query), 300);
    return () => clearTimeout(timer);
  }, [query]);

  const trimmedQuery = debouncedQuery.trim();
  const searchEnabled = open && trimmedQuery.length >= MIN_SEARCH_LENGTH;

  const candidatesQuery = useQuery({
    queryKey: queryKeys.authorHardcoverMatchCandidates(authorId, trimmedQuery),
    queryFn: () => browseApi.getAuthorHardcoverMatchCandidates(authorId, trimmedQuery),
    enabled: searchEnabled,
  });

  const candidates: AuthorMatchCandidate[] = candidatesQuery.data ?? [];

  const handleMatch = async (candidate: AuthorMatchCandidate) => {
    setMatching(true);
    setMatchingSourceId(candidate.sourceId);
    try {
      // The backend is persist-first: MatchAuthor stores the source link and THEN refreshes the
      // roster (which can take seconds). A refresh failure - daily budget exhausted, the source
      // cannot resolve the id, no author-capable scraper - comes back as a 200 with
      // success=false, since the match itself was stored. Either way the author IS matched, so
      // the match/detail/upcoming queries are invalidated and the dialog closes; only the
      // notification differs, and the periodic sweep picks the roster up on its next tick.
      // Without this, a refresh failure left the dialog open and the author shown as unmatched
      // until a full reload, and every retry repeated the same refresh failure.
      const result = await browseApi.matchAuthorToHardcover(
        authorId,
        candidate.sourceId,
        candidate.sourceName,
        candidate.sourceUrl ?? undefined,
      );
      await queryClient.invalidateQueries({ queryKey: queryKeys.authorHardcoverMatch(authorId) });
      await queryClient.invalidateQueries({ queryKey: queryKeys.author.all() });
      await queryClient.invalidateQueries({ queryKey: queryKeys.upcomingReleases.all() });
      if (result.success) {
        notifications.success(`Matched to ${candidate.sourceName}: ${candidate.name}`);
      } else {
        notifications.warning(
          `Matched to ${candidate.sourceName}: ${candidate.name}, but the roster refresh failed. The next scheduled refresh will pick it up.`,
        );
      }
      onOpenChange(false);
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setMatching(false);
      setMatchingSourceId(null);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="flex max-h-[80dvh] w-[calc(100vw-2rem)] flex-col overflow-hidden sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Match {authorName} to Hardcover</DialogTitle>
        </DialogHeader>

        <div className="flex-1 space-y-3 overflow-y-auto py-2 text-sm">
          <div className="flex items-center gap-2">
            <Search className="text-muted-foreground h-4 w-4 shrink-0" />
            <Input
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Search Hardcover authors..."
            />
          </div>

          {!searchEnabled ? (
            <p className="text-muted-foreground py-6 text-center">
              Type at least {MIN_SEARCH_LENGTH} characters to search.
            </p>
          ) : candidatesQuery.isLoading ? (
            <div className="text-muted-foreground flex items-center justify-center py-8">
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              Searching...
            </div>
          ) : candidatesQuery.isError ? (
            <div className="text-destructive flex items-center justify-center gap-1.5 py-6 text-center">
              <AlertCircle className="h-4 w-4 shrink-0" />
              {handleApiError(candidatesQuery.error).message}
            </div>
          ) : candidates.length === 0 ? (
            <p className="text-muted-foreground py-6 text-center">No matching authors found.</p>
          ) : (
            <div className="divide-y rounded-md border">
              {candidates.map((candidate) => (
                <button
                  key={candidate.sourceId}
                  type="button"
                  disabled={matching}
                  onClick={() => {
                    void handleMatch(candidate);
                  }}
                  className="hover:bg-muted/50 flex w-full items-center justify-between gap-2 p-2.5 text-left transition-colors disabled:opacity-50"
                >
                  <span className="font-medium">{candidate.name}</span>
                  <span className="flex items-center gap-1.5">
                    {matchingSourceId === candidate.sourceId && (
                      <Loader2 className="text-muted-foreground h-3.5 w-3.5 animate-spin" />
                    )}
                    {candidate.bookCount != null && (
                      <span className="text-muted-foreground text-xs">
                        {candidate.bookCount} books
                      </span>
                    )}
                  </span>
                </button>
              ))}
            </div>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}

export default AuthorFollowSection;
