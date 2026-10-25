import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowDown, ArrowUp, ListOrdered, Loader2, Save } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { metadataSearchApi, settingsApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";
import { moveItem, sortServicesByOrder } from "@/helpers/metadataSources";

/**
 * The order the online metadata sources are listed in wherever they appear: the source pickers,
 * the tabs of search results and pending matches, the source filters. The sources themselves (and
 * their current order) come from the server, so nothing about them is hardcoded here.
 */
export function MetadataSourceOrderCard() {
  const queryClient = useQueryClient();
  // The order the user has arranged, or null until they touch it, so the saved order shows through.
  const [draft, setDraft] = useState<string[] | null>(null);

  const { data: services, isLoading } = useQuery({
    queryKey: queryKeys.metadataServices(),
    queryFn: () => metadataSearchApi.getServices(),
  });

  const saved = (services ?? []).map((s) => s.name);
  const order = draft ?? saved;
  // The sources can change under an open draft (a refetch after a scraper was registered or
  // removed), so a different set counts as a change, not only a different order.
  const changed =
    draft !== null && (draft.length !== saved.length || draft.some((name, i) => name !== saved[i]));

  const mutation = useMutation({
    mutationFn: (sources: string[]) => settingsApi.updateMetadataSourceOrder(sources),
    onSuccess: (result) => {
      notifications.success("Source order saved");
      setDraft(null);
      // Show the stored order straight away rather than the old one until the refetch lands.
      queryClient.setQueryData(queryKeys.metadataServices(), (current: typeof services) =>
        current ? sortServicesByOrder(current, result.sources) : current,
      );
      // Everything that lists sources was read in the old order.
      void queryClient.invalidateQueries({ queryKey: queryKeys.metadataServices() });
      void queryClient.invalidateQueries({ queryKey: queryKeys.browseFilterOptions() });
      void queryClient.invalidateQueries({ queryKey: queryKeys.pendingOnlineMatch.all() });
    },
    onError: (err: unknown) => {
      notifications.error(handleApiError(err).message);
    },
  });

  const move = (index: number, delta: number) => setDraft(moveItem(order, index, delta));

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg">
          <ListOrdered className="text-primary h-5 w-5" />
          Source order
        </CardTitle>
        <CardDescription>
          The order online metadata sources are listed in everywhere in the app: the source pickers,
          the tabs of search results and pending matches, and the source filters.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        {isLoading || !services ? (
          <div className="text-muted-foreground flex items-center justify-center py-8">
            <Loader2 className="text-primary mr-2 h-5 w-5 animate-spin" />
            <span className="text-sm">Loading sources...</span>
          </div>
        ) : (
          <>
            <ol className="space-y-2">
              {order.map((name, index) => (
                <li
                  key={name}
                  className="flex items-center gap-2 rounded-md border px-3 py-2 text-sm"
                >
                  <span className="text-muted-foreground w-5 text-right text-xs">{index + 1}</span>
                  <span className="flex-1 font-medium">{name}</span>
                  <Button
                    type="button"
                    variant="outline"
                    size="icon"
                    aria-label={`Move ${name} up`}
                    disabled={mutation.isPending || index === 0}
                    onClick={() => move(index, -1)}
                  >
                    <ArrowUp className="h-4 w-4" />
                  </Button>
                  <Button
                    type="button"
                    variant="outline"
                    size="icon"
                    aria-label={`Move ${name} down`}
                    disabled={mutation.isPending || index === order.length - 1}
                    onClick={() => move(index, 1)}
                  >
                    <ArrowDown className="h-4 w-4" />
                  </Button>
                </li>
              ))}
            </ol>

            <div className="flex justify-end">
              <Button
                type="button"
                disabled={mutation.isPending || !changed}
                onClick={() => mutation.mutate(order)}
              >
                {mutation.isPending ? (
                  <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />
                ) : (
                  <Save className="mr-1.5 h-4 w-4" />
                )}
                Save order
              </Button>
            </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}

export default MetadataSourceOrderCard;
