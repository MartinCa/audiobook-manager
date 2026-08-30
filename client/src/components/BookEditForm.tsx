import { useEffect, useMemo, useState, type ReactNode } from "react";
import { useForm, Controller, useWatch } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useQuery } from "@tanstack/react-query";
import { Search, RotateCcw, ExternalLink } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
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
import { audiobookApi, settingsApi } from "@/services/api";
import {
  joinList,
  splitList,
  cleanDescription,
  normalizeSeriesPart,
} from "@/helpers/organizeAudiobookInput";
import { normalizeLanguage, languageSelectItems } from "@/helpers/languages";
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
  genres: z.string(),
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
    genres: (book.genres || []).join(" / "),
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
    authors: splitList(values.authors).map((name) => ({ name })),
    narrators: splitList(values.narrators).map((name) => ({ name })),
    bookName: values.bookName.trim(),
    subtitle: values.subtitle.trim() || undefined,
    series: values.series.trim() || undefined,
    seriesPart: values.seriesPart.trim() || undefined,
    year: values.year ? parseInt(values.year, 10) : undefined,
    genres: values.genres
      .split("/")
      .map((g) => g.trim())
      .filter(Boolean),
    description: values.description.trim() || undefined,
    copyright: values.copyright.trim() || undefined,
    publisher: values.publisher.trim() || undefined,
    language: values.language.trim() || undefined,
    rating: values.rating.trim() || undefined,
    asin: values.asin.trim() || undefined,
    www: values.www.trim() || undefined,
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
  toolbarActions,
  formActions,
  defaultEmptyLanguage = false,
}: BookEditFormProps) {
  const [cover, setCover] = useState<AudiobookImage | undefined>(initialBook.cover);
  const [newPath, setNewPath] = useState<string | null>(null);
  const [searchDialogOpen, setSearchDialogOpen] = useState(false);
  const [saving, setSaving] = useState(false);

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
    const values: BookEditFormValues = { ...valuesFromBook(initialBook), ...watchedValues };
    if (!values.bookName.trim() || !values.authors.trim()) return;

    const book = buildAudiobook(values, cover, initialBook);
    const timer = setTimeout(() => {
      void audiobookApi
        .generateNewPath(book)
        .then((generated) => setNewPath(generated))
        .catch(() => {});
    }, 300);
    return () => clearTimeout(timer);
  }, [watchedValues, cover, initialBook]);

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
      genres: watchedValues.genres,
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
      form.setValue("genres", result.genres.join(" / "), { shouldDirty: true });
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
      form.setValue("language", result.language, { shouldDirty: true });
    }
    if (selectedFields.has("rating") && result.rating) {
      form.setValue("rating", String(result.rating), { shouldDirty: true });
    }
    if (selectedFields.has("asin") && result.asin) {
      form.setValue("asin", result.asin, { shouldDirty: true });
    }
    if (selectedFields.has("www") && result.url) {
      form.setValue("www", result.url, { shouldDirty: true });
    }

    const coverUrlToFetch = result.imageUrl;
    if (selectedFields.has("cover") && coverUrlToFetch) {
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
    } finally {
      setSaving(false);
    }
  };

  const handleReset = () => {
    form.reset(valuesFromBook(initialBook));
    setCover(initialBook.cover);
    onReset?.();
  };

  return (
    <form
      onSubmit={(e) => {
        void form.handleSubmit(handleValidSubmit)(e);
      }}
      className="space-y-6"
    >
      <div className="border-border flex flex-wrap items-center justify-between gap-2 border-b pb-4">
        <Button type="button" variant="outline" onClick={() => setSearchDialogOpen(true)}>
          <Search className="text-primary mr-2 h-4 w-4" />
          Search Online Metadata
        </Button>
        <div className="flex items-center gap-2">{toolbarActions}</div>
      </div>

      {currentPath && (
        <div className="space-y-1">
          <label className="text-muted-foreground text-xs font-semibold uppercase">
            File Location / Target Path
          </label>
          {newPath && newPath !== currentPath ? (
            <DiffDisplay actual={currentPath} expected={newPath} />
          ) : (
            <div className="border-border bg-muted/40 text-muted-foreground rounded-md border p-2 font-mono text-xs break-all">
              {currentPath}
            </div>
          )}
        </div>
      )}

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
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div>
              <label className="mb-1 block text-xs font-medium">
                Authors <span className="text-destructive">*</span>
              </label>
              <Input
                {...form.register("authors")}
                placeholder="Author Name, Second Author"
                aria-invalid={Boolean(form.formState.errors.authors)}
              />
              {form.formState.errors.authors && (
                <p className="text-destructive mt-1 text-xs">
                  {form.formState.errors.authors.message}
                </p>
              )}
            </div>

            <div>
              <label className="mb-1 block text-xs font-medium">Narrators</label>
              <Input {...form.register("narrators")} placeholder="Narrator Name" />
            </div>

            <div>
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

            <div>
              <label className="mb-1 block text-xs font-medium">Subtitle</label>
              <Input {...form.register("subtitle")} placeholder="Subtitle" />
            </div>

            <div>
              <label className="mb-1 block text-xs font-medium">Series</label>
              <Input {...form.register("series")} placeholder="Series name" />
            </div>

            <div>
              <label className="mb-1 block text-xs font-medium">Series Part / Book #</label>
              <Input {...form.register("seriesPart")} placeholder="e.g. 1 or 2.5" />
            </div>

            <div>
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

            <div>
              <label className="mb-1 block text-xs font-medium">Genres (separated by /)</label>
              <Input {...form.register("genres")} placeholder="Fantasy / Fiction" />
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

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <div>
              <label className="mb-1 block text-xs font-medium">Language</label>
              <Controller
                control={form.control}
                name="language"
                render={({ field }) => {
                  const items = languageSelectItems(field.value, languages);
                  return (
                    <Select
                      value={field.value}
                      onValueChange={field.onChange}
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

            <div>
              <label className="mb-1 block text-xs font-medium">Publisher</label>
              <Input {...form.register("publisher")} placeholder="Publisher" />
            </div>

            <div>
              <label className="mb-1 block text-xs font-medium">Copyright</label>
              <Input {...form.register("copyright")} placeholder="Copyright year / owner" />
            </div>

            <div>
              <label className="mb-1 block text-xs font-medium">Rating</label>
              <Input {...form.register("rating")} placeholder="e.g. 4.5" />
            </div>

            <div>
              <label className="mb-1 block text-xs font-medium">ASIN</label>
              <Input {...form.register("asin")} placeholder="B0..." />
            </div>

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
          </div>
        </div>
      </div>

      <div className="border-border flex flex-wrap items-center justify-between gap-4 border-t pt-4">
        <Button type="button" variant="outline" onClick={handleReset}>
          <RotateCcw className="mr-2 h-4 w-4" />
          Reset
        </Button>

        <div className="flex items-center gap-2">
          {formActions || (
            <Button type="submit" disabled={saving}>
              {saving ? "Saving..." : "Save Audiobook"}
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
