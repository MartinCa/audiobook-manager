import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { BellRing, Bell, Link2, Link2Off, Loader2, Search } from "lucide-react";
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

function AuthorMatchDialog({ authorId, authorName, open, onOpenChange }: AuthorMatchDialogProps) {
  const queryClient = useQueryClient();
  const [query, setQuery] = useState(authorName);
  const [matching, setMatching] = useState(false);

  const candidatesQuery = useQuery({
    queryKey: queryKeys.authorHardcoverMatchCandidates(authorId, query),
    queryFn: () => browseApi.getAuthorHardcoverMatchCandidates(authorId, query),
    enabled: open,
  });

  const candidates: AuthorMatchCandidate[] = candidatesQuery.data ?? [];

  const handleMatch = async (candidate: AuthorMatchCandidate) => {
    setMatching(true);
    try {
      await browseApi.matchAuthorToHardcover(
        authorId,
        candidate.sourceId,
        "Hardcover",
        candidate.sourceUrl ?? undefined,
      );
      await queryClient.invalidateQueries({ queryKey: queryKeys.authorHardcoverMatch(authorId) });
      notifications.success(`Matched to Hardcover: ${candidate.name}`);
      onOpenChange(false);
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setMatching(false);
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

          {candidatesQuery.isLoading ? (
            <div className="text-muted-foreground flex items-center justify-center py-8">
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              Searching...
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
                  {candidate.bookCount != null && (
                    <span className="text-muted-foreground text-xs">
                      {candidate.bookCount} books
                    </span>
                  )}
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
