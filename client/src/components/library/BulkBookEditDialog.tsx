import { useMemo, useState, type ReactNode } from "react";
import { Controller, useForm, useWatch, type FieldNamesMarkedBoolean } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, Loader2, RefreshCw } from "lucide-react";
import { z } from "zod";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Checkbox } from "@/components/ui/checkbox";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { TagsInput } from "@/components/tags-input";
import { AuthorsField } from "@/components/fields/AuthorsField";
import { NarratorsField } from "@/components/fields/NarratorsField";
import { SeriesField } from "@/components/fields/SeriesField";
import { LanguageField } from "@/components/fields/LanguageField";
import { bulkEditApi } from "@/services/api";
import { computeMultiFieldState, computeSingleFieldState } from "@/helpers/bulkEdit";
import { handleApiError } from "@/lib/api";
import { toast } from "sonner";
import { cn } from "@/lib/utils";
import type { SelectedBookInfo } from "@/hooks/useBookSelection";
import type { BulkEditPreviewBook } from "@/types/BulkEdit";
import type { BulkEditAudiobooksRequest } from "@/types/BulkEdit";

export interface BulkBookEditDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  selectedBooks: SelectedBookInfo[];
}

// Every field a bulk edit can change. Multi fields (authors, narrators, genres) carry a
// replace/add mode instead of a value; the single fields map to the DTO's BulkEditSingleValueDto.
interface BulkEditFormValues {
  authors: string[];
  narrators: string[];
  genres: string[];
  bookName: string;
  subtitle: string;
  series: string;
  seriesPart: string;
  year: string;
  description: string;
  language: string;
  publisher: string;
  copyright: string;
  rating: string;
  asin: string;
  www: string;
}

type MultiFieldKey = "authors" | "narrators" | "genres";
type SingleFieldKey =
  | "subtitle"
  | "series"
  | "seriesPart"
  | "description"
  | "language"
  | "publisher"
  | "copyright"
  | "rating"
  | "asin"
  | "www";

// Every field that can carry a "Clear" checkbox: the clearable single fields plus the two
// multi-value fields whose emptiness is legitimate (narrators, genres).
type ClearableFieldKey = SingleFieldKey | "narrators" | "genres";

type MultiMode = "replace" | "add";

interface MultiFieldModes {
  authors: MultiMode;
  narrators: MultiMode;
  genres: MultiMode;
}

// The single fields whose clear the backend forbids (no title, no year) plus Authors, whose list
// must never empty, render without a "Clear" control - they can only be set. Narrators and Genres
// accept the explicit clear action (the one empty-list form the backend allows) and get the same
// "Clear" checkbox as the clearable single fields.
const CLEARABLE_SINGLE_FIELDS: SingleFieldKey[] = [
  "subtitle",
  "series",
  "seriesPart",
  "description",
  "language",
  "publisher",
  "copyright",
  "rating",
  "asin",
  "www",
];

const CLEARABLE_MULTI_FIELDS = ["narrators", "genres"] as const;

const bulkEditFormSchema = z.object({
  authors: z.array(z.string()),
  narrators: z.array(z.string()),
  genres: z.array(z.string()),
  bookName: z.string(),
  subtitle: z.string(),
  series: z.string(),
  seriesPart: z.string(),
  year: z.string().refine(
    (v) => {
      const trimmed = v.trim();
      return trimmed === "" || (/^[0-9]+$/.test(trimmed) && Number(trimmed) > 0);
    },
    { message: "Year must be a positive whole number" },
  ),
  description: z.string(),
  language: z.string(),
  publisher: z.string(),
  copyright: z.string(),
  rating: z.string(),
  asin: z.string(),
  www: z.string(),
});

type BuiltChanges = Omit<BulkEditAudiobooksRequest, "audiobookIds">;

// The payload contract riders, re-checked against the backend's MapBulkChanges:
//  - a single field that is not dirty, or whose typed value trims to empty, is absent: leaving a
//    field empty must never clear it — the explicit "Clear" checkbox is the only way to clear,
//  - a multi field is sent for replace/add only when it is dirty AND non-empty (an empty replace
//    list is refused), while a cleared narrators/genres field sends the one empty-list form the
//    backend does accept: { action: "clear", values: [] },
//  - Authors is never cleared: it stays critical, so a book always keeps at least one author.
function buildChanges(
  values: BulkEditFormValues,
  dirtyFields: FieldNamesMarkedBoolean<BulkEditFormValues>,
  cleared: ReadonlySet<ClearableFieldKey>,
  modes: MultiFieldModes,
): BuiltChanges {
  const changes: BuiltChanges = {};

  for (const field of CLEARABLE_SINGLE_FIELDS) {
    if (cleared.has(field)) {
      changes[field] = { action: "clear" };
    } else if (dirtyFields[field] && values[field].trim() !== "") {
      changes[field] = { action: "set", value: values[field].trim() };
    }
  }

  if (dirtyFields.bookName && values.bookName.trim() !== "") {
    changes.bookName = { action: "set", value: values.bookName.trim() };
  }

  if (dirtyFields.year && values.year.trim() !== "") {
    changes.year = { action: "set", value: Number(values.year.trim()) };
  }

  if (dirtyFields.authors && values.authors.length > 0) {
    changes.authors = { action: modes.authors, values: values.authors };
  }
  for (const field of CLEARABLE_MULTI_FIELDS) {
    if (cleared.has(field)) {
      changes[field] = { action: "clear", values: [] };
    } else if (dirtyFields[field] && values[field].length > 0) {
      changes[field] = { action: modes[field], values: values[field] };
    }
  }

  return changes;
}

function countActions(changes: BuiltChanges): { setCount: number; clearedCount: number } {
  let setCount = 0;
  let clearedCount = 0;
  for (const change of Object.values(changes)) {
    if (change?.action === "clear") {
      clearedCount++;
    } else {
      setCount++;
    }
  }
  return { setCount, clearedCount };
}

function defaultsFromBooks(books: BulkEditPreviewBook[]): BulkEditFormValues {
  const single = (pick: (b: BulkEditPreviewBook) => string | null | undefined) =>
    computeSingleFieldState(books, pick).common ?? "";
  const multi = (pick: (b: BulkEditPreviewBook) => string[] | undefined) =>
    computeMultiFieldState(books, pick).common ?? [];
  return {
    authors: multi((b) => b.authors),
    narrators: multi((b) => b.narrators),
    genres: multi((b) => b.genres),
    bookName: single((b) => b.bookName),
    subtitle: single((b) => b.subtitle),
    series: single((b) => b.series),
    seriesPart: single((b) => b.seriesPart),
    year: single((b) => (b.year != null ? String(b.year) : null)),
    description: single((b) => b.description),
    language: single((b) => b.language),
    publisher: single((b) => b.publisher),
    copyright: single((b) => b.copyright),
    rating: single((b) => b.rating),
    asin: single((b) => b.asin),
    www: single((b) => b.www),
  };
}

export function BulkBookEditDialog({ open, onOpenChange, selectedBooks }: BulkBookEditDialogProps) {
  const ids = useMemo(() => selectedBooks.map((b) => b.id), [selectedBooks]);
  const idsKey = ids.join(",");
  const count = selectedBooks.length;

  const previewQuery = useQuery({
    // ids (not just their joined string) joins the key so the exhaustive-deps rule can see every
    // value the queryFn closes over.
    queryKey: ["bulkEditPreview", idsKey, ids],
    queryFn: () => bulkEditApi.preview(ids),
    enabled: open && ids.length > 0,
  });

  const previewTitles = selectedBooks.map((b) => b.title).filter(Boolean);
  const shownTitles = previewTitles.slice(0, 3);
  const subtitle =
    shownTitles.length > 0
      ? `${shownTitles.join(", ")}${previewTitles.length > shownTitles.length ? `… and ${previewTitles.length - shownTitles.length} more` : ""}`
      : count === 1
        ? "One book selected"
        : `${count} books selected`;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="flex max-h-[85vh] w-[calc(100vw-2rem)] flex-col overflow-hidden p-4 sm:max-w-3xl sm:p-6">
        <DialogHeader>
          <DialogTitle>Edit Metadata for {count} Books</DialogTitle>
          <DialogDescription className="truncate">{subtitle}</DialogDescription>
        </DialogHeader>

        {previewQuery.isLoading && !previewQuery.data ? (
          <div
            role="status"
            aria-label="Loading bulk edit preview"
            className="flex-1 space-y-3 overflow-y-auto py-3"
          >
            {Array.from({ length: 6 }, (_, i) => (
              <div key={i} className="bg-muted h-10 animate-pulse rounded-md" />
            ))}
          </div>
        ) : previewQuery.isError ? (
          <div className="flex-1 space-y-3 overflow-y-auto py-3">
            <Alert variant="destructive">
              <AlertTriangle className="h-4 w-4" />
              <AlertTitle>Couldn&apos;t load the preview</AlertTitle>
              <AlertDescription>
                {handleApiError(previewQuery.error).message} The selection was not changed; try
                again.
              </AlertDescription>
            </Alert>
            <Button
              variant="outline"
              size="sm"
              disabled={previewQuery.isFetching}
              onClick={() => void previewQuery.refetch()}
            >
              <RefreshCw className="mr-1.5 h-3.5 w-3.5" />
              Retry
            </Button>
          </div>
        ) : previewQuery.data ? (
          <BulkEditForm
            key={idsKey}
            books={previewQuery.data.books}
            selectedBooks={selectedBooks}
            ids={ids}
            onOpenChange={onOpenChange}
          />
        ) : null}
      </DialogContent>
    </Dialog>
  );
}

function BulkEditForm({
  books,
  selectedBooks,
  ids,
  onOpenChange,
}: {
  books: BulkEditPreviewBook[];
  selectedBooks: SelectedBookInfo[];
  ids: number[];
  onOpenChange: (open: boolean) => void;
}) {
  const count = selectedBooks.length;

  const form = useForm<BulkEditFormValues>({
    resolver: zodResolver(bulkEditFormSchema),
    defaultValues: defaultsFromBooks(books),
  });

  const watchedValues = useWatch({ control: form.control }) as BulkEditFormValues;
  const [cleared, setCleared] = useState<Set<ClearableFieldKey>>(() => new Set());
  const [modes, setModes] = useState<MultiFieldModes>({
    authors: "replace",
    narrators: "replace",
    genres: "replace",
  });
  const [submitting, setSubmitting] = useState(false);

  const toggleCleared = (field: ClearableFieldKey, nowCleared: boolean) => {
    setCleared((prev) => {
      const next = new Set(prev);
      if (nowCleared) {
        next.add(field);
      } else {
        next.delete(field);
      }
      return next;
    });
  };

  const setMode = (field: MultiFieldKey, mode: MultiMode) => {
    setModes((prev) => ({ ...prev, [field]: mode }));
  };

  const states = useMemo(
    () => ({
      authors: computeMultiFieldState(books, (b) => b.authors),
      narrators: computeMultiFieldState(books, (b) => b.narrators),
      genres: computeMultiFieldState(books, (b) => b.genres),
      bookName: computeSingleFieldState(books, (b) => b.bookName),
      subtitle: computeSingleFieldState(books, (b) => b.subtitle),
      series: computeSingleFieldState(books, (b) => b.series),
      seriesPart: computeSingleFieldState(books, (b) => b.seriesPart),
      year: computeSingleFieldState(books, (b) => (b.year != null ? String(b.year) : null)),
      description: computeSingleFieldState(books, (b) => b.description),
      language: computeSingleFieldState(books, (b) => b.language),
      publisher: computeSingleFieldState(books, (b) => b.publisher),
      copyright: computeSingleFieldState(books, (b) => b.copyright),
      rating: computeSingleFieldState(books, (b) => b.rating),
      asin: computeSingleFieldState(books, (b) => b.asin),
      www: computeSingleFieldState(books, (b) => b.www),
    }),
    [books],
  );

  const singlePlaceholder = (mixed: boolean, fallback: string) =>
    mixed ? "Different values" : fallback;

  const changes = useMemo(
    () => buildChanges(watchedValues, form.formState.dirtyFields, cleared, modes),
    [watchedValues, form.formState.dirtyFields, cleared, modes],
  );
  const { setCount, clearedCount } = useMemo(() => countActions(changes), [changes]);
  const hasChanges = setCount > 0 || clearedCount > 0;

  const handleValidSubmit = async () => {
    if (!hasChanges) return;
    setSubmitting(true);
    try {
      await bulkEditApi.apply(ids, { ...changes, audiobookIds: ids });
      toast.success(`Bulk edit started for ${count} books`);
      onOpenChange(false);
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <form
      onSubmit={(e) => {
        void form.handleSubmit(handleValidSubmit)(e);
      }}
      className="flex min-h-0 flex-1 flex-col overflow-hidden"
    >
      <div className="min-h-0 flex-1 space-y-4 overflow-y-auto py-2 pr-1">
        <div>
          <p className="text-muted-foreground mb-1.5 text-[11px] font-semibold uppercase">
            Affected books
          </p>
          <div className="border-border bg-muted/20 max-h-24 space-y-1 overflow-y-auto rounded-md border p-2 pr-1">
            {selectedBooks.map((bookInfo) => (
              <div key={bookInfo.id} className="text-muted-foreground truncate text-xs">
                {bookInfo.authors.length > 0 ? `${bookInfo.authors.join(", ")} — ` : ""}
                {bookInfo.title}
              </div>
            ))}
          </div>
          <p className="text-muted-foreground mt-1.5 text-[11px]">
            Each book&apos;s m4b file will be re-tagged with the new values and may be relocated to
            a new library path.
          </p>
        </div>

        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <FieldRow label="Authors" critical>
            <Controller
              control={form.control}
              name="authors"
              render={({ field }) => (
                <AuthorsField
                  value={field.value ?? []}
                  onChange={field.onChange}
                  showLabel={false}
                  placeholder={singlePlaceholder(
                    states.authors.mixed,
                    "Author Name, Second Author",
                  )}
                />
              )}
            />
            <MultiModeRadio
              groupId="authors"
              value={modes.authors}
              onValueChange={(mode) => setMode("authors", mode)}
            />
          </FieldRow>

          <FieldRow
            label="Narrators"
            cleared={cleared.has("narrators")}
            onClearChange={(c) => toggleCleared("narrators", c)}
          >
            <Controller
              control={form.control}
              name="narrators"
              render={({ field }) => (
                <NarratorsField
                  value={field.value ?? []}
                  onChange={field.onChange}
                  disabled={cleared.has("narrators")}
                  showLabel={false}
                  placeholder={singlePlaceholder(states.narrators.mixed, "Narrator Name")}
                />
              )}
            />
            {!cleared.has("narrators") && (
              <MultiModeRadio
                groupId="narrators"
                value={modes.narrators}
                onValueChange={(mode) => setMode("narrators", mode)}
              />
            )}
          </FieldRow>

          <FieldRow label="Book Title" critical>
            <Input
              {...form.register("bookName")}
              placeholder={singlePlaceholder(states.bookName.mixed, "Book title")}
              aria-invalid={Boolean(form.formState.errors.bookName)}
            />
            {form.formState.errors.bookName && (
              <p className="text-destructive mt-1 text-xs">
                {form.formState.errors.bookName.message}
              </p>
            )}
          </FieldRow>

          <FieldRow
            label="Subtitle"
            cleared={cleared.has("subtitle")}
            onClearChange={(c) => toggleCleared("subtitle", c)}
          >
            <Input
              {...form.register("subtitle")}
              placeholder={singlePlaceholder(states.subtitle.mixed, "Subtitle")}
              disabled={cleared.has("subtitle")}
            />
          </FieldRow>

          <FieldRow
            label="Series"
            cleared={cleared.has("series")}
            onClearChange={(c) => toggleCleared("series", c)}
          >
            <Controller
              control={form.control}
              name="series"
              render={({ field }) => (
                <SeriesField
                  value={field.value ?? ""}
                  onChange={field.onChange}
                  disabled={cleared.has("series")}
                  showLabel={false}
                  placeholder={singlePlaceholder(states.series.mixed, "Series name")}
                />
              )}
            />
          </FieldRow>

          <FieldRow
            label="Series Part"
            cleared={cleared.has("seriesPart")}
            onClearChange={(c) => toggleCleared("seriesPart", c)}
          >
            <Input
              {...form.register("seriesPart")}
              placeholder={singlePlaceholder(states.seriesPart.mixed, "e.g. 1 or 2.5")}
              disabled={cleared.has("seriesPart")}
            />
          </FieldRow>

          <FieldRow label="Year" critical>
            <Input
              type="number"
              {...form.register("year")}
              placeholder={singlePlaceholder(states.year.mixed, "YYYY")}
              aria-invalid={Boolean(form.formState.errors.year)}
            />
            {form.formState.errors.year && (
              <p className="text-destructive mt-1 text-xs">{form.formState.errors.year.message}</p>
            )}
          </FieldRow>

          <FieldRow
            label="Genres"
            cleared={cleared.has("genres")}
            onClearChange={(c) => toggleCleared("genres", c)}
          >
            <Controller
              control={form.control}
              name="genres"
              render={({ field }) => (
                <TagsInput
                  value={field.value ?? []}
                  onValueChange={field.onChange}
                  disabled={cleared.has("genres")}
                  placeholder={singlePlaceholder(states.genres.mixed, "Fantasy, Fiction")}
                />
              )}
            />
            {!cleared.has("genres") && (
              <MultiModeRadio
                groupId="genres"
                value={modes.genres}
                onValueChange={(mode) => setMode("genres", mode)}
              />
            )}
          </FieldRow>

          <FieldRow
            label="Description"
            className="md:col-span-2"
            cleared={cleared.has("description")}
            onClearChange={(c) => toggleCleared("description", c)}
          >
            <Textarea
              {...form.register("description")}
              rows={3}
              placeholder={singlePlaceholder(
                states.description.mixed,
                "Book summary or description",
              )}
              disabled={cleared.has("description")}
            />
          </FieldRow>

          <FieldRow
            label="Language"
            cleared={cleared.has("language")}
            onClearChange={(c) => toggleCleared("language", c)}
          >
            <Controller
              control={form.control}
              name="language"
              render={({ field }) => (
                <LanguageField
                  value={field.value || ""}
                  onChange={field.onChange}
                  disabled={cleared.has("language")}
                  showLabel={false}
                  placeholder={singlePlaceholder(states.language.mixed, "Select language...")}
                />
              )}
            />
          </FieldRow>

          <FieldRow
            label="Publisher"
            cleared={cleared.has("publisher")}
            onClearChange={(c) => toggleCleared("publisher", c)}
          >
            <Input
              {...form.register("publisher")}
              placeholder={singlePlaceholder(states.publisher.mixed, "Publisher")}
              disabled={cleared.has("publisher")}
            />
          </FieldRow>

          <FieldRow
            label="Copyright"
            cleared={cleared.has("copyright")}
            onClearChange={(c) => toggleCleared("copyright", c)}
          >
            <Input
              {...form.register("copyright")}
              placeholder={singlePlaceholder(states.copyright.mixed, "Copyright year / owner")}
              disabled={cleared.has("copyright")}
            />
          </FieldRow>

          <FieldRow
            label="Rating"
            cleared={cleared.has("rating")}
            onClearChange={(c) => toggleCleared("rating", c)}
          >
            <Input
              {...form.register("rating")}
              placeholder={singlePlaceholder(states.rating.mixed, "e.g. 4.5")}
              disabled={cleared.has("rating")}
            />
          </FieldRow>

          <FieldRow
            label="ASIN"
            cleared={cleared.has("asin")}
            onClearChange={(c) => toggleCleared("asin", c)}
          >
            <Input
              {...form.register("asin")}
              placeholder={singlePlaceholder(states.asin.mixed, "B0...")}
              disabled={cleared.has("asin")}
            />
          </FieldRow>

          <FieldRow
            label="WWW"
            cleared={cleared.has("www")}
            onClearChange={(c) => toggleCleared("www", c)}
          >
            <Input
              {...form.register("www")}
              placeholder={singlePlaceholder(states.www.mixed, "https://...")}
              disabled={cleared.has("www")}
            />
          </FieldRow>
        </div>
      </div>

      <div className="border-border border-t pt-3">
        {hasChanges ? (
          <p className="text-muted-foreground text-xs">
            {setCount} field{setCount === 1 ? "" : "s"} will be set
            {clearedCount > 0 ? ` · ${clearedCount} cleared` : ""} across {count} books
          </p>
        ) : (
          <p className="text-muted-foreground text-xs">
            Enter a value or mark fields to clear — empty fields are left unchanged.
          </p>
        )}

        <DialogFooter className="mt-3">
          <Button
            type="button"
            variant="outline"
            className="w-full sm:w-auto"
            disabled={submitting}
            onClick={() => onOpenChange(false)}
          >
            Cancel
          </Button>
          <Button type="submit" className="w-full sm:w-auto" disabled={!hasChanges || submitting}>
            {submitting ? (
              <>
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                Applying...
              </>
            ) : (
              "Apply"
            )}
          </Button>
        </DialogFooter>
      </div>
    </form>
  );
}

function FieldRow({
  label,
  critical = false,
  cleared,
  onClearChange,
  className,
  children,
}: {
  label: string;
  critical?: boolean;
  cleared?: boolean;
  onClearChange?: (nowCleared: boolean) => void;
  className?: string;
  children: ReactNode;
}) {
  const clearable = !critical && onClearChange !== undefined;
  return (
    <div className={cn("min-w-0", className)}>
      <div className="mb-1 flex items-center justify-between gap-2">
        <label className="text-xs font-medium">
          {label}
          {critical && <span className="text-destructive"> *</span>}
        </label>
        {clearable && (
          <label className="text-muted-foreground flex cursor-pointer items-center gap-1.5 text-xs leading-none select-none">
            <Checkbox
              checked={Boolean(cleared)}
              onCheckedChange={(c) => onClearChange?.(Boolean(c))}
              aria-label={`Clear ${label}`}
            />
            {/* aria-hidden so the checkbox's accessible name stays exactly "Clear <field>" (the
                wrapping label's text would otherwise be appended to it). */}
            <span aria-hidden="true">Clear</span>
          </label>
        )}
      </div>
      {children}
      {Boolean(cleared) && (
        <p className="text-muted-foreground mt-1.5 text-[11px] italic">
          Will be cleared on all books
        </p>
      )}
    </div>
  );
}

function MultiModeRadio({
  groupId,
  value,
  onValueChange,
}: {
  groupId: string;
  value: MultiMode;
  onValueChange: (mode: MultiMode) => void;
}) {
  return (
    <RadioGroup
      value={value}
      onValueChange={(next: MultiMode) => onValueChange(next)}
      className="mt-1.5 flex flex-row gap-4"
    >
      <label
        htmlFor={`${groupId}-replace`}
        className="text-muted-foreground flex cursor-pointer items-center gap-1.5 text-xs"
      >
        <RadioGroupItem value="replace" id={`${groupId}-replace`} />
        Replace all values
      </label>
      <label
        htmlFor={`${groupId}-add`}
        className="text-muted-foreground flex cursor-pointer items-center gap-1.5 text-xs"
      >
        <RadioGroupItem value="add" id={`${groupId}-add`} />
        Add to existing values
      </label>
    </RadioGroup>
  );
}

export default BulkBookEditDialog;
