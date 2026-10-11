import { useMemo, useState, type ReactNode } from "react";
import { MetadataSearchResultCard } from "@/components/MetadataSearchResultCard";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import type { MetadataSearchResult } from "@/types/MetadataSearchResult";

interface SourceTabbedResultsProps {
  results: MetadataSearchResult[];
  /** Every source that was searched, in display order. A source with no results still gets a
   * (disabled) tab so it is clear it was asked. Sources found only in `results` are appended. */
  sources?: string[];
  /** The caller's action for a result. `index` is the result's position in the original flat
   * `results` array - flows that select by position (pending online matches) need that, not the
   * position within a tab. */
  renderActions: (result: MetadataSearchResult, index: number) => ReactNode;
}

/**
 * Candidate results grouped into one tab per source, each labelled with its result count, so a
 * source far down a long flat list (Hardcover, say) is one tap away. Shared by every flow that
 * lets the user choose a match: BookSearchDialog and the pending online-match row panel.
 * Tab order follows `sources` (which callers take from the backend), never a hardcoded list.
 */
export function SourceTabbedResults({ results, sources, renderActions }: SourceTabbedResultsProps) {
  const groups = useMemo(() => {
    const byName = new Map<string, { result: MetadataSearchResult; index: number }[]>();
    for (const name of sources ?? []) byName.set(name, []);
    results.forEach((result, index) => {
      const name = result.source ?? "Unknown";
      const group = byName.get(name);
      if (group) group.push({ result, index });
      else byName.set(name, [{ result, index }]);
    });
    return [...byName.entries()].map(([name, items]) => ({ name, items }));
  }, [results, sources]);

  const [chosen, setChosen] = useState<string | null>(null);
  const firstWithResults = groups.find((g) => g.items.length > 0)?.name ?? null;
  // A chosen tab that no longer exists or has emptied out (new search) falls back to the first
  // populated one rather than leaving a blank panel.
  const active =
    chosen && groups.some((g) => g.name === chosen && g.items.length > 0)
      ? chosen
      : firstWithResults;

  const renderItems = (items: { result: MetadataSearchResult; index: number }[]) => (
    <div className="space-y-2">
      {items.map(({ result, index }) => (
        <MetadataSearchResultCard
          key={`${result.source}-${result.bookName}-${index}`}
          result={result}
          actions={renderActions(result, index)}
        />
      ))}
    </div>
  );

  if (groups.length <= 1) return renderItems(groups[0]?.items ?? []);

  return (
    <Tabs value={active} onValueChange={(value) => setChosen(String(value))}>
      <TabsList
        variant="line"
        className="border-border h-auto! w-full min-w-0 justify-start gap-1 overflow-x-auto border-b"
      >
        {groups.map((g) => (
          <TabsTrigger
            key={g.name}
            value={g.name}
            disabled={g.items.length === 0}
            className="min-h-9 flex-none px-3"
          >
            {g.name} ({g.items.length})
          </TabsTrigger>
        ))}
      </TabsList>
      {groups.map((g) => (
        <TabsContent key={g.name} value={g.name}>
          {renderItems(g.items)}
        </TabsContent>
      ))}
    </Tabs>
  );
}

export default SourceTabbedResults;
