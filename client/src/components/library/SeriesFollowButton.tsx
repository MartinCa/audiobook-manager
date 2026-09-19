import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Bell, BellRing } from "lucide-react";
import { Button } from "@/components/ui/button";
import { seriesApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";

interface SeriesFollowButtonProps {
  seriesName: string;
  /** Called after a successful follow/unfollow - the series detail page uses it to refetch the
   * overview, since following creates the (previously absent) catalog row a never-matched
   * series' upcoming-releases section needs. */
  onChanged?: () => void;
}

/** Follows this series for upcoming releases. Uses the series' existing Hardcover match (see
 * SeriesMatchDialog) - there is no separate match step here. */
export function SeriesFollowButton({ seriesName, onChanged }: SeriesFollowButtonProps) {
  const queryClient = useQueryClient();
  const [busy, setBusy] = useState(false);

  const followQuery = useQuery({
    queryKey: queryKeys.seriesFollow(seriesName),
    queryFn: () => seriesApi.getFollowStatus(seriesName),
  });

  const isFollowed = followQuery.data?.isFollowed ?? false;

  const handleToggle = async () => {
    setBusy(true);
    try {
      if (isFollowed) {
        await seriesApi.unfollowSeries(seriesName);
      } else {
        await seriesApi.followSeries(seriesName);
      }
      await queryClient.invalidateQueries({ queryKey: queryKeys.seriesFollow(seriesName) });
      onChanged?.();
    } catch (err: unknown) {
      notifications.error(handleApiError(err).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Button
      variant={isFollowed ? "default" : "outline"}
      size="sm"
      disabled={busy || followQuery.isLoading}
      onClick={() => {
        void handleToggle();
      }}
    >
      {isFollowed ? (
        <BellRing className="mr-1.5 h-3.5 w-3.5" />
      ) : (
        <Bell className="mr-1.5 h-3.5 w-3.5" />
      )}
      {isFollowed ? "Following" : "Follow for upcoming releases"}
    </Button>
  );
}

export default SeriesFollowButton;
