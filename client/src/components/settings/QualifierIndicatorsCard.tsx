import { useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Loader2, Plus, Save, Tags, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { useBookQualifiers } from "@/hooks/useBookQualifiers";
import { metadataSearchApi, settingsApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";

interface Row {
  id: number;
  source: string;
  indicator: string;
  qualifierKey: string;
}

/**
 * The per-source wordings that stand for a book qualifier. A title scraped from a source that
 * carries one of these in brackets - "[Dramatized Adaptation]", "(Abridged)" - arrives with the
 * wording removed and the qualifier set; the metadata diff then offers it as an optional, additive
 * field. The list of sources and of qualifiers both come from the backend, so neither is hardcoded
 * here.
 */
export function QualifierIndicatorsCard() {
  const queryClient = useQueryClient();
  const nextId = useRef(0);
  const qualifierOptions = useBookQualifiers();
  // Rows the user has edited; null until they touch anything, so the saved list shows through.
  const [draft, setDraft] = useState<Row[] | null>(null);

  const { data, isLoading } = useQuery({
    queryKey: queryKeys.qualifierIndicators(),
    queryFn: () => settingsApi.getQualifierIndicators(),
  });
  const { data: services } = useQuery({
    queryKey: queryKeys.metadataServices(),
    queryFn: () => metadataSearchApi.getServices(),
    staleTime: Infinity,
  });

  const sources = (services ?? []).map((s) => s.name);

  const savedRows: Row[] = (data?.indicators ?? []).map((i, index) => ({
    id: -1 - index,
    source: i.source,
    indicator: i.indicator,
    qualifierKey: i.qualifierKey,
  }));
  const rows = draft ?? savedRows;

  const mutation = useMutation({
    mutationFn: (toSave: Row[]) =>
      settingsApi.updateQualifierIndicators(
        toSave.map(({ source, indicator, qualifierKey }) => ({ source, indicator, qualifierKey })),
      ),
    onSuccess: () => {
      notifications.success("Qualifier indicators saved");
      setDraft(null);
      void queryClient.invalidateQueries({ queryKey: queryKeys.qualifierIndicators() });
    },
    onError: (err: unknown) => {
      notifications.error(handleApiError(err).message);
    },
  });

  const update = (id: number, patch: Partial<Row>) =>
    setDraft(rows.map((r) => (r.id === id ? { ...r, ...patch } : r)));

  const addRow = () =>
    setDraft([
      ...rows,
      {
        id: nextId.current++,
        source: sources[0] ?? "",
        indicator: "",
        qualifierKey: qualifierOptions[0]?.key ?? "",
      },
    ]);

  const hasBlank = rows.some((r) => !r.indicator.trim() || !r.source || !r.qualifierKey);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg">
          <Tags className="text-primary h-5 w-5" />
          Qualifier indicators
        </CardTitle>
        <CardDescription>
          Online sources spell out abridged and dramatized editions in the title, each in their own
          words. Say which wording means which qualifier for a source: a title such as{" "}
          <span className="font-mono">A Frontier Christmas [Dramatized Adaptation]</span> is then
          fetched as &quot;A Frontier Christmas&quot; with the Dramatized qualifier, offered as an
          optional change when you apply online metadata. Qualifiers a book already has are never
          removed. Audible&apos;s &quot;Abridged Audiobook&quot; format is recognised on its own.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        {isLoading ? (
          <div className="text-muted-foreground flex items-center justify-center py-8">
            <Loader2 className="text-primary mr-2 h-5 w-5 animate-spin" />
            <span className="text-sm">Loading indicators...</span>
          </div>
        ) : (
          <>
            {rows.length === 0 && (
              <p className="text-muted-foreground text-sm">No indicators configured.</p>
            )}
            {rows.map((row) => (
              <div key={row.id} className="flex flex-col gap-2 sm:flex-row sm:items-center">
                <Select
                  value={row.source || undefined}
                  onValueChange={(v) => {
                    if (v != null) update(row.id, { source: v });
                  }}
                  items={sources.map((s) => ({ value: s, label: s }))}
                  disabled={mutation.isPending}
                >
                  <SelectTrigger className="w-full sm:w-40" aria-label="Source">
                    <SelectValue placeholder="Source" />
                  </SelectTrigger>
                  <SelectContent>
                    {sources.map((s) => (
                      <SelectItem key={s} value={s}>
                        {s}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <Input
                  value={row.indicator}
                  onChange={(e) => update(row.id, { indicator: e.target.value })}
                  placeholder="Dramatized Adaptation"
                  aria-label="Indicator"
                  disabled={mutation.isPending}
                  className="w-full font-mono sm:flex-1"
                />
                <span className="text-muted-foreground hidden text-sm sm:inline">means</span>
                <Select
                  value={row.qualifierKey || undefined}
                  onValueChange={(v) => {
                    if (v != null) update(row.id, { qualifierKey: v });
                  }}
                  items={qualifierOptions.map((q) => ({ value: q.key, label: q.label }))}
                  disabled={mutation.isPending}
                >
                  <SelectTrigger className="w-full sm:w-40" aria-label="Qualifier">
                    <SelectValue placeholder="Qualifier" />
                  </SelectTrigger>
                  <SelectContent>
                    {qualifierOptions.map((q) => (
                      <SelectItem key={q.key} value={q.key}>
                        {q.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  aria-label="Remove indicator"
                  disabled={mutation.isPending}
                  onClick={() => setDraft(rows.filter((r) => r.id !== row.id))}
                >
                  <Trash2 className="h-4 w-4" />
                </Button>
              </div>
            ))}
            <p className="text-muted-foreground text-xs">
              Write the wording with or without its brackets; both () and [] are matched, ignoring
              case. Only a whole bracketed group counts, so &quot;(Part 1 of 2)&quot; is never
              touched.
            </p>
            <div className="flex flex-wrap items-center justify-between gap-2">
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={addRow}
                disabled={mutation.isPending || sources.length === 0}
              >
                <Plus className="mr-1 h-3 w-3" />
                Add indicator
              </Button>
              <div className="flex items-center gap-2">
                {hasBlank && (
                  <span className="text-muted-foreground text-xs">
                    Fill in or remove empty rows to save.
                  </span>
                )}
                <Button
                  type="button"
                  size="sm"
                  disabled={mutation.isPending || draft === null || hasBlank}
                  onClick={() => mutation.mutate(rows)}
                >
                  {mutation.isPending ? (
                    <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />
                  ) : (
                    <Save className="mr-1.5 h-4 w-4" />
                  )}
                  Save indicators
                </Button>
              </div>
            </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}

export default QualifierIndicatorsCard;
