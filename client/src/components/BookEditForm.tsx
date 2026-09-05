import { useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
import { useForm, Controller, useWatch } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Search,
  RotateCcw,
  ExternalLink,
  Trash2,
  Save,
  Loader2,
  ChevronDown,
  ChevronUp,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { TagsInput } from "@/components/tags-input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { CoverEditor } from "./CoverEditor";
import { BookSearchDialog } from "./BookSearchDialog";
import { TagPreviewDialog } from "./TagPreviewDialog";
import { DiffDisplay } from "./DiffDisplay";
import { TypeaheadInput } from "./TypeaheadInput";
import { audiobookApi, settingsApi, similarValuesApi } from "@/services/api";
import {
  joinList,
  splitList,
  cleanDescription,
  normalizeSeriesPart,
  DEFAULT_COLLAPSED_FIELDS,
  type CollapsedField,
} from "@/helpers/organizeAudiobookInput";
import { normalizeLanguage, languageSelectItems } from "@/helpers/languages";
import { findSimilarExisting } from "@/helpers/similarValueMatcher";
import type { Audiobook, AudiobookImage } from "@/types/Audiobook";
import type { MetadataSearchResult } from "@/types/MetadataSearchResult";
import type { LanguageOption } from "@/types/Language";
import type { OrganizeAudiobookInput } from "@/types/OrganizeAudiobookInput";

// DESIGN.md section 1: forms use react-hook-form + zod for client-side validation. The
// server validates independently; this only stops an obviously incomplete submit early.
const bookEditFormSchema = z.object({
  authors: z.string().trim().min(1, "At least one author is required"),
  narrators: z.string(),
  bookName: z.string().trim().min(1, "Book title is required"),
  subtitle: z.string(),
  series: z.string(),
  seriesPart: z.string(),
  year: z
    .string()
    .trim()
    .min(1, "Year is required")
    .refine((v) => Number.isFinite(Number(v)), "Year must be a number"),
  genres: z.array(z.string()),
  description: z.string(),
  copyright: z.string(),
  publisher: z.string(),
  language: z.string(),
  rating: z.string(),
  asin: z.string(),
  www: z.string(),
});

type BookEditFormValues = z.infer<typeof bookEditFormSchema>;

function valuesFromBook(book: Audiobook): BookEditFormValues {
  return {
    authors: joinList(book.authors?.map((a) => a.name)),
    narrators: joinList(book.narrators?.map((n) => n.name)),
    bookName: book.bookName || "",
    subtitle: book.subtitle || "",
    series: book.series || "",
    seriesPart: book.seriesPart || "",
    year: book.year ? String(book.year) : "",
    genres: book.genres || [],
    description: book.description || "",
    copyright: book.copyright || "",
    publisher: book.publisher || "",
    language: book.language || "",
    rating: book.rating || "",
    asin: book.asin || "",
    www: book.www || "",
  };
}

function buildAudiobook(
  values: BookEditFormValues,
  cover: AudiobookImage | undefined,
  initialBook: Audiobook,
): Audiobook {
  return {
    authors: splitList(values.authors ?? "").map((name) => ({ name })),
    narrators: splitList(values.narrators ?? "").map((name) => ({ name })),
    bookName: (values.bookName ?? "").trim(),
    subtitle: values.subtitle?.trim() || undefined,
    series: values.series?.trim() || undefined,
    seriesPart: values.seriesPart?.trim() || undefined,
    year: values.year ? parseInt(values.year, 10) : undefined,
    genres: values.genres ?? [],
    description: values.description?.trim() || undefined,
    copyright: values.copyright?.trim() || undefined,
    publisher: values.publisher?.trim() || undefined,
    language: values.language?.trim() || undefined,
    rating: values.rating?.trim() || undefined,
    asin: values.asin?.trim() || undefined,
    www: values.www?.trim() || undefined,
    cover,
    fileInfo: initialBook.fileInfo,
    durationInSeconds: initialBook.durationInSeconds,
  };
}

export interface BookEditFormProps {
  initialBook: Audiobook;
  currentPath?: string;
  coverUrl?: string;
  onSave: (book: Audiobook) => void | Promise<void>;
  onReset?: () => void;
  onDelete?: () => void;
  deleteLabel?: string;
  deleteDisabled?: boolean;
  submitLabel?: string;
  submitIcon?: ReactNode;
  isSaving?: boolean;
  toolbarActions?: ReactNode;
  formActions?: ReactNode;
  /**
   * Seed an empty language with the backend's default code once the language list loads.
   * Only set by the organize workflow (importing a new file) — a book already in the library
   * never gets this default, or opening its edit page would silently grant it a language and
   * hide it from Missing Tags.
   */
  defaultEmptyLanguage?: boolean;
}

export function BookEditForm({
  initialBook,
  currentPath,
  coverUrl,
  onSave,
  onReset,
  onDelete,
  deleteLabel,
  deleteDisabled = false,
  submitLabel,
  submitIcon,
  isSaving = false,
  toolbarActions,
  formActions,
  defaultEmptyLanguage = false,
}: BookEditFormProps) {
  const [cover, setCover] = useState<AudiobookImage | undefined>(initialBook.cover);
  const [newPath, setNewPath] = useState<string | null>(null);
  const [searchDialogOpen, setSearchDialogOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const [authorHint, setAuthorHint] = useState<string | null>(null);
  const [seriesHint, setSeriesHint] = useState<string | null>(null);
  const [showAllOptionalFields, setShowAllOptionalFields] = useState(false);
  const queryClient = useQueryClient();

  // Entry-time duplicate prevention: flat name lists to check a typed Author/Series value
  // against. Non-critical — the hint below just won't show if this fails to load.
  const { data: authorNames = [] } = useQuery({
    queryKey: ["similarValueNames", "authors"],
    queryFn: () => similarValuesApi.getAuthorNames(),
    staleTime: 5 * 60 * 1000,
  });
  const { data: seriesNames = [] } = useQuery({
    queryKey: ["similarValueNames", "series"],
    queryFn: () => similarValuesApi.getSeriesNames(),
    staleTime: 5 * 60 * 1000,
  });

  const form = useForm<BookEditFormValues>({
    resolver: zodResolver(bookEditFormSchema),
    defaultValues: valuesFromBook(initialBook),
  });

  const { data: languagesRes } = useQuery({
    queryKey: ["languages"],
    queryFn: () => settingsApi.getLanguages(),
  });
  const languages: LanguageOption[] = languagesRes?.languages ?? [];

  const watchedValues = useWatch({ control: form.control });

  const isFieldVisible = useCallback(
    (field: CollapsedField) => {
      if (showAllOptionalFields) return true;
      const val = watchedValues[field];
      return Boolean(val && String(val).trim().length > 0);
    },
    [showAllOptionalFields, watchedValues],
  );

  const hiddenFieldsCount = useMemo(
    () => DEFAULT_COLLAPSED_FIELDS.filter((f) => !isFieldVisible(f)).length,
    [isFieldVisible],
  );

  useEffect(() => {
    if (!languagesRes) return;
    const current = form.getValues("language");
    const normalized = normalizeLanguage(current, languagesRes.languages);
    if (normalized) {
      if (normalized !== current) form.setValue("language", normalized);
    } else if (defaultEmptyLanguage && !current) {
      form.setValue("language", languagesRes.defaultCode || "");
    }
    // Only re-run when the language list itself arrives/changes, not on every keystroke.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [languagesRes]);

  useEffect(() => {
    let cancelled = false;
    const values: BookEditFormValues = { ...valuesFromBook(initialBook), ...watchedValues };
    if (!values.bookName?.trim() || !values.authors?.trim()) return;

    const book = buildAudiobook(values, undefined, initialBook);
    const timer = setTimeout(() => {
      void audiobookApi
        .generateNewPath(book)
        .then((generated) => {
          if (!cancelled) {
            setNewPath(generated);
          }
        })
        .catch(() => {});
    }, 300);
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [watchedValues, initialBook]);

  const [tagPreviewOpen, setTagPreviewOpen] = useState(false);
  const [pendingSearchResult, setPendingSearchResult] = useState<MetadataSearchResult | null>(null);

  const currentOrganizeInput: OrganizeAudiobookInput = useMemo(
    () => ({
      authors: watchedValues.authors,
      narrators: watchedValues.narrators,
      bookName: watchedValues.bookName,
      subtitle: watchedValues.subtitle,
      series: watchedValues.series,
      seriesPart: watchedValues.seriesPart,
      year: watchedValues.year ? parseInt(watchedValues.year, 10) : undefined,
      genres: watchedValues.genres?.join("/"),
      description: watchedValues.description,
      copyright: watchedValues.copyright,
      publisher: watchedValues.publisher,
      language: watchedValues.language,
      rating: watchedValues.rating ? Number(watchedValues.rating) : undefined,
      asin: watchedValues.asin,
      www: watchedValues.www,
      cover_base64: cover?.base64Data,
      cover_mime: cover?.mimeType,
    }),
    [watchedValues, cover],
  );

  const handleSelectSearchResult = (result: MetadataSearchResult) => {
    setPendingSearchResult(result);
    setTagPreviewOpen(true);
  };

  const handleApplyPreviewedTags = (result: MetadataSearchResult, selectedFields: Set<string>) => {
    if (selectedFields.has("bookName") && result.bookName) {
      form.setValue("bookName", result.bookName, { shouldDirty: true });
    }
    if (selectedFields.has("subtitle") && result.subtitle) {
      form.setValue("subtitle", result.subtitle, { shouldDirty: true });
    }
    if (selectedFields.has("authors") && result.authors && result.authors.length > 0) {
      form.setValue("authors", joinList(result.authors.map((a) => a.name)), {
        shouldDirty: true,
        shouldValidate: true,
      });
    }
    if (selectedFields.has("narrators") && result.narrators && result.narrators.length > 0) {
      form.setValue("narrators", joinList(result.narrators.map((n) => n.name)), {
        shouldDirty: true,
      });
    }
    if (selectedFields.has("series")) {
      const firstSeries = result.series?.[0];
      const sName = firstSeries?.seriesName;
      const sPart = firstSeries?.seriesPart;
      if (sName !== undefined) form.setValue("series", sName || "", { shouldDirty: true });
      if (sPart !== undefined) {
        form.setValue("seriesPart", normalizeSeriesPart(sPart || ""), { shouldDirty: true });
      }
    }
    if (selectedFields.has("year") && result.year) {
      form.setValue("year", String(result.year), { shouldDirty: true, shouldValidate: true });
    }
    if (selectedFields.has("genres") && result.genres && result.genres.length > 0) {
      form.setValue("genres", result.genres, { shouldDirty: true });
    }
    if (selectedFields.has("description") && result.description) {
      form.setValue("description", cleanDescription(result.description), { shouldDirty: true });
    }
    if (selectedFields.has("copyright") && result.copyright) {
      form.setValue("copyright", result.copyright, { shouldDirty: true });
    }
    if (selectedFields.has("publisher") && result.publisher) {
      form.setValue("publisher", result.publisher, { shouldDirty: true });
    }
    if (selectedFields.has("language") && result.language) {
      const normalizedLang =
        normalizeLanguage(result.language, languages) ?? result.language.trim();
      form.setValue("language", normalizedLang, { shouldDirty: true });
    }
    if (selectedFields.has("rating") && result.rating) {
      form.setValue("rating", String(result.rating), { shouldDirty: true });
    }
    if (selectedFields.has("asin") && result.asin) {
      form.setValue("asin", result.asin, { shouldDirty: true });
    }
    if (selectedFields.has("www") && result.cleanUrl) {
      form.setValue("www", result.cleanUrl, { shouldDirty: true });
    }

    const coverUrlToFetch = result.imageUrl;
    if (selectedFields.has("cover") && coverUrlToFetch) {
      // lib/api.ts parses every response as JSON; this needs the raw image blob. A GET, so
      // the backend's write guard does not apply to it.
      // eslint-disable-next-line no-restricted-globals -- binary response, see above
      void fetch(`/api/metadata-search/proxy-image?url=${encodeURIComponent(coverUrlToFetch)}`)
        .then((res) => res.blob())
        .then((blob) => {
          const reader = new FileReader();
          reader.onloadend = () => {
            const resStr = reader.result as string;
            const idx = resStr.indexOf(";base64,");
            const clean = idx !== -1 ? resStr.substring(idx + 8) : resStr;
            setCover({
              base64Data: clean,
              mimeType: blob.type || "image/jpeg",
            });
          };
          reader.readAsDataURL(blob);
        })
        .catch(() => {});
    }
  };

  const handleCoverUpdate = (base64Data: string | undefined, mimeType: string | undefined) => {
    if (base64Data && mimeType) {
      setCover({ base64Data, mimeType });
    } else {
      setCover(undefined);
    }
  };

  const handleValidSubmit = async (values: BookEditFormValues) => {
    setSaving(true);
    try {
      await onSave(buildAudiobook(values, cover, initialBook));
      // A newly-typed author/series is now a real value in the backend; refresh the cached
      // name lists so the next book's entry-time duplicate-prevention hint can see it.
      void queryClient.invalidateQueries({ queryKey: ["similarValueNames"] });
    } finally {
      setSaving(false);
    }
  };

  const handleReset = () => {
    form.reset(valuesFromBook(initialBook));
    setCover(initialBook.cover);
    setShowAllOptionalFields(false);
    onReset?.();
  };

  return (
    <form
      onSubmit={(e) => {
        void form.handleSubmit(handleValidSubmit)(e);
      }}
      className="space-y-6"
    >
      <div className="border-border flex flex-col justify-between gap-3 border-b pb-4 sm:flex-row sm:items-center">
        <Button
          type="button"
          variant="outline"
          onClick={() => setSearchDialogOpen(true)}
          className="w-full sm:w-auto"
        >
          <Search className="text-primary mr-2 h-4 w-4" />
          Search Online Metadata
        </Button>
        {toolbarActions && (
          <div className="flex w-full items-center justify-end gap-2 sm:w-auto">
            {toolbarActions}
          </div>
        )}
      </div>

      {currentPath && newPath && newPath !== currentPath ? (
        <div className="space-y-1">
          <label className="text-muted-foreground text-xs font-semibold uppercase">
            File Location / Target Path
          </label>
          <DiffDisplay actual={currentPath} expected={newPath} />
        </div>
      ) : null}

      <div className="grid grid-cols-1 gap-6 md:grid-cols-4">
        <div className="md:col-span-1">
          <CoverEditor
            base64Data={cover?.base64Data}
            mimeType={cover?.mimeType}
            coverUrl={!cover?.base64Data ? coverUrl : undefined}
            onCoverChange={handleCoverUpdate}
          />
        </div>

        <div className="space-y-4 md:col-span-3">
          <div className="flex flex-col gap-4 sm:flex-row">
            <div className="min-w-0 flex-1">
              <label className="mb-1 block text-xs font-medium">
                Authors <span className="text-destructive">*</span>
              </label>
              <Controller
                control={form.control}
                name="authors"
                render={({ field }) => (
                  <TypeaheadInput
                    ref={field.ref}
                    value={field.value ?? ""}
                    onValueChange={(val) => field.onChange(val)}
                    candidates={authorNames}
                    multiValue={true}
                    placeholder="Author Name, Second Author"
                    aria-invalid={Boolean(form.formState.errors.authors)}
                    onBlur={(e) => {
                      field.onBlur();
                      const primaryAuthor = splitList(e.target.value)[0];
                      const matches = findSimilarExisting(primaryAuthor, authorNames);
                      setAuthorHint(matches[0] && matches[0] !== primaryAuthor ? matches[0] : null);
                    }}
                  />
                )}
              />
              {form.formState.errors.authors && (
                <p className="text-destructive mt-1 text-xs">
                  {form.formState.errors.authors.message}
                </p>
              )}
              {authorHint && (
                <button
                  type="button"
                  className="text-muted-foreground hover:text-foreground mt-1 text-xs underline decoration-dotted"
                  onClick={() => {
                    form.setValue("authors", authorHint, {
                      shouldDirty: true,
                      shouldValidate: true,
                    });
                    setAuthorHint(null);
                  }}
                >
                  Similar existing author: {authorHint} (click to use)
                </button>
              )}
            </div>

            {isFieldVisible("narrators") && (
              <div className="min-w-0 flex-1">
                <label className="mb-1 block text-xs font-medium">Narrators</label>
                <Input {...form.register("narrators")} placeholder="Narrator Name" />
              </div>
            )}
          </div>

          <div className="flex flex-col gap-4 sm:flex-row">
            <div className="min-w-0 flex-1">
              <label className="mb-1 block text-xs font-medium">
                Book Title <span className="text-destructive">*</span>
              </label>
              <Input
                {...form.register("bookName")}
                placeholder="Book title"
                aria-invalid={Boolean(form.formState.errors.bookName)}
              />
              {form.formState.errors.bookName && (
                <p className="text-destructive mt-1 text-xs">
                  {form.formState.errors.bookName.message}
                </p>
              )}
            </div>

            {isFieldVisible("subtitle") && (
              <div className="min-w-0 flex-1">
                <label className="mb-1 block text-xs font-medium">Subtitle</label>
                <Input {...form.register("subtitle")} placeholder="Subtitle" />
              </div>
            )}
          </div>

          <div className="flex flex-col gap-4 sm:flex-row">
            <div className="min-w-0 flex-1">
              <label className="mb-1 block text-xs font-medium">Series</label>
              <Controller
                control={form.control}
                name="series"
                render={({ field }) => (
                  <TypeaheadInput
                    ref={field.ref}
                    value={field.value ?? ""}
                    onValueChange={(val) => field.onChange(val)}
                    candidates={seriesNames}
                    placeholder="Series name"
                    onBlur={(e) => {
                      field.onBlur();
                      const matches = findSimilarExisting(e.target.value, seriesNames);
                      setSeriesHint(
                        matches[0] && matches[0] !== e.target.value ? matches[0] : null,
                      );
                    }}
                  />
                )}
              />
              {seriesHint && (
                <button
                  type="button"
                  className="text-muted-foreground hover:text-foreground mt-1 text-xs underline decoration-dotted"
                  onClick={() => {
                    form.setValue("series", seriesHint, { shouldDirty: true });
                    setSeriesHint(null);
                  }}
                >
                  Similar existing series: {seriesHint} (click to use)
                </button>
              )}
            </div>

            <div className="min-w-0 flex-1">
              <label className="mb-1 block text-xs font-medium">Series Part / Book #</label>
              <Input {...form.register("seriesPart")} placeholder="e.g. 1 or 2.5" />
            </div>
          </div>

          <div className="flex flex-col gap-4 sm:flex-row">
            <div className="min-w-0 flex-1">
              <label className="mb-1 block text-xs font-medium">
                Year <span className="text-destructive">*</span>
              </label>
              <Input
                type="number"
                {...form.register("year")}
                placeholder="YYYY"
                aria-invalid={Boolean(form.formState.errors.year)}
              />
              {form.formState.errors.year && (
                <p className="text-destructive mt-1 text-xs">
                  {form.formState.errors.year.message}
                </p>
              )}
            </div>

            <div className="min-w-0 flex-1">
              <label className="mb-1 block text-xs font-medium">Genres</label>
              <Controller
                control={form.control}
                name="genres"
                render={({ field }) => (
                  <TagsInput
                    value={field.value ?? []}
                    onValueChange={field.onChange}
                    placeholder="Fantasy, Fiction"
                  />
                )}
              />
            </div>
          </div>

          <div>
            <label className="mb-1 block text-xs font-medium">Description</label>
            <Textarea
              {...form.register("description")}
              rows={4}
              placeholder="Book summary or description"
            />
          </div>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
            <div>
              <label className="mb-1 block text-xs font-medium">Language</label>
              <Controller
                control={form.control}
                name="language"
                render={({ field }) => {
                  const items = languageSelectItems(field.value, languages);
                  return (
                    <Select
                      value={field.value || ""}
                      onValueChange={(val) => field.onChange(val ?? "")}
                      items={items.map((l) => ({ value: l.code, label: l.displayName }))}
                    >
                      <SelectTrigger>
                        <SelectValue placeholder="Select language..." />
                      </SelectTrigger>
                      <SelectContent>
                        {items.map((l) => (
                          <SelectItem key={l.code} value={l.code}>
                            {l.displayName}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  );
                }}
              />
            </div>

            {isFieldVisible("publisher") && (
              <div>
                <label className="mb-1 block text-xs font-medium">Publisher</label>
                <Input {...form.register("publisher")} placeholder="Publisher" />
              </div>
            )}

            {isFieldVisible("copyright") && (
              <div>
                <label className="mb-1 block text-xs font-medium">Copyright</label>
                <Input {...form.register("copyright")} placeholder="Copyright year / owner" />
              </div>
            )}

            {isFieldVisible("rating") && (
              <div>
                <label className="mb-1 block text-xs font-medium">Rating</label>
                <Input {...form.register("rating")} placeholder="e.g. 4.5" />
              </div>
            )}

            {isFieldVisible("asin") && (
              <div>
                <label className="mb-1 block text-xs font-medium">ASIN</label>
                <Input {...form.register("asin")} placeholder="B0..." />
              </div>
            )}

            {isFieldVisible("www") && (
              <div>
                <label className="mb-1 flex items-center justify-between text-xs font-medium">
                  <span>Web link / URL</span>
                  {watchedValues.www && (
                    <a
                      href={watchedValues.www}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="text-primary flex items-center hover:underline"
                    >
                      <ExternalLink className="mr-0.5 h-3 w-3" /> Preview
                    </a>
                  )}
                </label>
                <Input {...form.register("www")} placeholder="https://..." />
              </div>
            )}
          </div>

          <div className="flex items-center pt-1">
            {hiddenFieldsCount > 0 ? (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="text-muted-foreground hover:text-foreground h-8 text-xs"
                onClick={() => setShowAllOptionalFields(true)}
              >
                <ChevronDown className="mr-1.5 h-3.5 w-3.5" />
                Show additional fields ({hiddenFieldsCount} hidden)
              </Button>
            ) : showAllOptionalFields ? (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="text-muted-foreground hover:text-foreground h-8 text-xs"
                onClick={() => setShowAllOptionalFields(false)}
              >
                <ChevronUp className="mr-1.5 h-3.5 w-3.5" />
                Hide empty optional fields
              </Button>
            ) : null}
          </div>
        </div>
      </div>

      <div className="border-border flex flex-col-reverse justify-between gap-3 border-t pt-4 sm:flex-row sm:items-center">
        <div className="flex flex-col-reverse gap-2 sm:flex-row sm:items-center">
          <Button
            type="button"
            variant="outline"
            onClick={handleReset}
            disabled={saving || isSaving}
            className="w-full sm:w-auto"
          >
            <RotateCcw className="mr-2 h-4 w-4" />
            Reset
          </Button>

          {onDelete && (
            <Button
              type="button"
              variant="outline"
              onClick={onDelete}
              disabled={deleteDisabled || saving || isSaving}
              className="text-destructive hover:bg-destructive/10 border-destructive/30 hover:border-destructive/60 w-full sm:w-auto"
            >
              <Trash2 className="mr-2 h-4 w-4" />
              {deleteLabel || "Delete File"}
            </Button>
          )}
        </div>

        <div className="flex w-full items-center justify-end gap-2 sm:w-auto">
          {formActions || (
            <Button type="submit" disabled={saving || isSaving} className="w-full sm:w-auto">
              {saving || isSaving ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                submitIcon || <Save className="mr-2 h-4 w-4" />
              )}
              {submitLabel || "Save Audiobook"}
            </Button>
          )}
        </div>
      </div>

      <BookSearchDialog
        open={searchDialogOpen}
        onOpenChange={setSearchDialogOpen}
        onSelectResult={handleSelectSearchResult}
        initialQuery={watchedValues.bookName || initialBook.fileInfo?.fileName || ""}
      />

      {pendingSearchResult && (
        <TagPreviewDialog
          open={tagPreviewOpen}
          onOpenChange={setTagPreviewOpen}
          currentInput={currentOrganizeInput}
          searchResult={pendingSearchResult}
          onApply={handleApplyPreviewedTags}
        />
      )}
    </form>
  );
}

export default BookEditForm;
