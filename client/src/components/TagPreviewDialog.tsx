import { useState, useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { settingsApi } from "@/services/api";
import { joinPersons } from "@/helpers/bookDetailsHelpers";
import { languageLabel, normalizeLanguage } from "@/helpers/languages";
import type { OrganizeAudiobookInput } from "@/types/OrganizeAudiobookInput";
import type { MetadataSearchResult } from "@/types/MetadataSearchResult";

interface TagPreviewDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  currentInput: OrganizeAudiobookInput;
  searchResult: MetadataSearchResult;
  onApply: (result: MetadataSearchResult, selectedFields: Set<string>) => void;
}

interface FieldDiff {
  key: string;
  label: string;
  currentValue: string;
  newValue: string;
  changed: boolean;
}

const truncate = (str: string, length: number): string => {
  if (str.length <= length) return str;
  return str.substring(0, length) + "...";
};

export function TagPreviewDialog({
  open,
  onOpenChange,
  currentInput,
  searchResult,
  onApply,
}: TagPreviewDialogProps) {
  const { data: langData } = useQuery({
    queryKey: ["languages"],
    queryFn: () => settingsApi.getLanguages(),
    staleTime: Infinity,
  });

  const languages = useMemo(() => langData?.languages ?? [], [langData?.languages]);

  const fields = useMemo((): FieldDiff[] => {
    const cur = currentInput;
    const res = searchResult;

    const newAuthors = joinPersons(res.authors) ?? "";
    const newNarrators = joinPersons(res.narrators) ?? "";
    const firstSeries = res.series?.[0];
    const newSeries = firstSeries?.seriesName ?? "";
    const newSeriesPart = firstSeries?.seriesPart ?? "";
    const newGenres = res.genres?.join("/") ?? "";

    const currentLanguage = normalizeLanguage(cur.language, languages) ?? cur.language ?? "";
    const newLanguage = normalizeLanguage(res.language, languages) ?? currentLanguage;

    return [
      {
        key: "authors",
        label: "Authors",
        currentValue: cur.authors ?? "",
        newValue: newAuthors,
        changed: (cur.authors ?? "") !== newAuthors,
      },
      {
        key: "narrators",
        label: "Narrators",
        currentValue: cur.narrators ?? "",
        newValue: newNarrators,
        changed: (cur.narrators ?? "") !== newNarrators,
      },
      {
        key: "bookName",
        label: "Book Name",
        currentValue: cur.bookName ?? "",
        newValue: res.bookName ?? "",
        changed: (cur.bookName ?? "") !== (res.bookName ?? ""),
      },
      {
        key: "subtitle",
        label: "Subtitle",
        currentValue: cur.subtitle ?? "",
        newValue: res.subtitle ?? "",
        changed: (cur.subtitle ?? "") !== (res.subtitle ?? ""),
      },
      {
        key: "series",
        label: "Series",
        currentValue: [cur.series, cur.seriesPart].filter(Boolean).join(" #") || "",
        newValue: [newSeries, newSeriesPart].filter(Boolean).join(" #") || "",
        changed: (cur.series ?? "") !== newSeries || (cur.seriesPart ?? "") !== newSeriesPart,
      },
      {
        key: "year",
        label: "Year",
        currentValue: cur.year?.toString() ?? "",
        newValue: res.year?.toString() ?? "",
        changed: cur.year !== res.year,
      },
      {
        key: "genres",
        label: "Genres",
        currentValue: cur.genres ?? "",
        newValue: newGenres,
        changed: (cur.genres ?? "") !== newGenres,
      },
      {
        key: "description",
        label: "Description",
        currentValue: truncate(cur.description ?? "", 100),
        newValue: truncate(res.description ?? "", 100),
        changed: (cur.description ?? "") !== (res.description ?? ""),
      },
      {
        key: "rating",
        label: "Rating",
        currentValue: cur.rating?.toString() ?? "",
        newValue: res.rating?.toString() ?? "",
        changed: cur.rating?.toString() !== res.rating?.toString(),
      },
      {
        key: "publisher",
        label: "Publisher",
        currentValue: cur.publisher ?? "",
        newValue: res.publisher ?? "",
        changed: (cur.publisher ?? "") !== (res.publisher ?? ""),
      },
      {
        key: "language",
        label: "Language",
        currentValue: languageLabel(currentLanguage, languages),
        newValue: languageLabel(newLanguage, languages),
        changed: currentLanguage !== newLanguage,
      },
      {
        key: "copyright",
        label: "Copyright",
        currentValue: cur.copyright ?? "",
        newValue: res.copyright ?? "",
        changed: (cur.copyright ?? "") !== (res.copyright ?? ""),
      },
      {
        key: "asin",
        label: "ASIN",
        currentValue: cur.asin ?? "",
        newValue: res.asin ?? "",
        changed: (cur.asin ?? "") !== (res.asin ?? ""),
      },
      {
        key: "www",
        label: "URL",
        currentValue: cur.www ?? "",
        newValue: res.url ?? "",
        changed: (cur.www ?? "") !== (res.url ?? ""),
      },
      {
        key: "cover",
        label: "Cover",
        currentValue: cur.cover_base64 ? "Has cover" : "",
        newValue: res.imageUrl ?? "",
        changed: Boolean(res.imageUrl),
      },
    ];
  }, [currentInput, searchResult, languages]);

  const changedFieldKeys = useMemo(
    () => fields.filter((f) => f.changed).map((f) => f.key),
    [fields],
  );

  const [selected, setSelected] = useState<Set<string>>(() => new Set());

  // Update selected when fields change
  const [lastSearchResult, setLastSearchResult] = useState<MetadataSearchResult | null>(null);
  if (searchResult !== lastSearchResult) {
    setLastSearchResult(searchResult);
    setSelected(new Set(changedFieldKeys));
  }

  const toggleField = (key: string) => {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(key)) {
        next.delete(key);
      } else {
        next.add(key);
      }
      return next;
    });
  };

  const toggleAll = () => {
    if (selected.size === changedFieldKeys.length) {
      setSelected(new Set());
    } else {
      setSelected(new Set(changedFieldKeys));
    }
  };

  const handleApplySelected = () => {
    onApply(searchResult, selected);
    onOpenChange(false);
  };

  const handleApplyAll = () => {
    const allKeys = new Set(fields.map((f) => f.key));
    onApply(searchResult, allKeys);
    onOpenChange(false);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="flex max-h-[90dvh] w-[calc(100vw-2rem)] flex-col overflow-hidden p-4 sm:max-w-3xl sm:p-6">
        <DialogHeader>
          <DialogTitle>Metadata Preview & Diff</DialogTitle>
        </DialogHeader>

        <div className="flex-1 space-y-4 overflow-y-auto py-2 text-xs">
          <p className="text-muted-foreground">
            Review scraped metadata from{" "}
            <strong className="text-foreground">{searchResult.source}</strong>. Select which fields
            you want to update.
          </p>

          <div className="flex flex-col justify-between gap-2 sm:flex-row sm:items-center">
            <Button
              variant="ghost"
              size="sm"
              className="h-7 w-full justify-start text-xs sm:w-auto sm:justify-center"
              onClick={toggleAll}
            >
              {selected.size === changedFieldKeys.length
                ? "Deselect All Changed"
                : "Select All Changed"}
            </Button>
            <span className="text-muted-foreground text-[11px] sm:text-xs">
              {selected.size} of {changedFieldKeys.length} changed fields selected
            </span>
          </div>

          <div className="border-border max-h-[50vh] overflow-x-auto overflow-y-auto rounded-md border">
            <table className="w-full border-collapse text-left text-xs">
              <thead className="bg-muted/70 text-muted-foreground sticky top-0 z-10 border-b">
                <tr>
                  <th className="w-8 p-2 text-center sm:w-10">Use</th>
                  <th className="w-20 p-2 sm:w-28">Field</th>
                  <th className="min-w-[90px] p-2">Current Value</th>
                  <th className="min-w-[120px] p-2">New Value</th>
                </tr>
              </thead>
              <tbody className="divide-border divide-y">
                {fields.map((field) => {
                  const isChecked = selected.has(field.key);
                  const isLinkOrCover = field.key === "www" || field.key === "cover";
                  return (
                    <tr
                      key={field.key}
                      className={
                        field.changed
                          ? "bg-muted/20 hover:bg-muted/40 font-medium"
                          : "text-muted-foreground hover:bg-muted/10 opacity-70"
                      }
                    >
                      <td className="p-2 text-center">
                        <Checkbox
                          checked={isChecked}
                          onCheckedChange={() => toggleField(field.key)}
                        />
                      </td>
                      <td className="text-foreground p-2 font-semibold">{field.label}</td>
                      <td
                        className={`p-2 ${
                          isLinkOrCover
                            ? "text-[11px] break-all"
                            : "max-w-[150px] break-words sm:max-w-[200px]"
                        }`}
                      >
                        {field.currentValue || (
                          <span className="text-muted-foreground italic">—</span>
                        )}
                      </td>
                      <td
                        className={`p-2 ${
                          isLinkOrCover
                            ? "text-[11px] break-all"
                            : "max-w-[200px] break-words sm:max-w-[300px]"
                        }`}
                      >
                        <span
                          className={
                            field.changed ? "text-primary font-bold dark:text-emerald-400" : ""
                          }
                        >
                          {field.newValue || (
                            <span className="text-muted-foreground italic">—</span>
                          )}
                        </span>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>

        <div className="border-border flex flex-col-reverse items-stretch justify-end gap-2 border-t pt-3 sm:flex-row sm:items-center sm:pt-4">
          <Button
            variant="outline"
            className="w-full sm:w-auto"
            onClick={() => onOpenChange(false)}
          >
            Cancel
          </Button>
          <Button
            variant="outline"
            disabled={selected.size === 0}
            className="w-full sm:w-auto"
            onClick={handleApplySelected}
          >
            Apply Selected ({selected.size})
          </Button>
          <Button className="w-full sm:w-auto" onClick={handleApplyAll}>
            Apply All
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}

export default TagPreviewDialog;
