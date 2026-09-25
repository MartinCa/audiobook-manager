import { createFileRoute } from "@tanstack/react-router";
import PendingOnlineMatches from "@/components/library/PendingOnlineMatches";

export const Route = createFileRoute("/library/pending-online-match")({
  component: PendingOnlineMatches,
});
