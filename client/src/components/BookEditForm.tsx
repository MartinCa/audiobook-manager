import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { useForm, useFieldArray, Controller, useWatch } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useQuery } from "@tanstack/react-query";
import {
  Search,
  RotateCcw,
  ExternalLink,
  Trash2,
  Save,
  Loader2,
  ChevronDown,
  ChevronUp,
  Info,
  AlertTriangle,
  Plus,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { splitTitleOnColon } from "@/helpers/titleSplitter";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { TagsInput } from "@/components/tags-input";
import { AuthorsField } from "@/components/fields/AuthorsField";
import { NarratorsField } from "@/components/fields/NarratorsField";
import { SeriesField } from "@/components/fields/SeriesField";
import { SeriesPartHints } from "@/components/fields/SeriesPartHints";
import { SeriesRelationRow } from "@/components/fields/SeriesRelationRow";
import { LanguageField } from "@/components/fields/LanguageField";
import { CoverEditor } from "./CoverEditor";
import { BookSearchDialog } from "./BookSearchDialog";
import { TagPreviewDialog, type TitleSplitOutcome } from "./TagPreviewDialog";
import { DiffDisplay } from "./DiffDisplay";
import { audiobookApi, settingsApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import {
  joinList,
  cleanDescription,
  normalizeSeriesPart,
  DEFAULT_COLLAPSED_FIELDS,
  type CollapsedField,
} from "@/helpers/organizeAudiobookInput";
import { buildDefaultMetadataSearchQuery } from "@/helpers/metadataSearchQuery";
import { normalizeLanguage } from "@/helpers/languages";
import {
  applyQualifiers,
  cleanSearchResult,
  normalizeQualifiers,
  qualifierLabel,
  splitQualifiers,
} from "@/helpers/bookQualifiers";
import { useBookQualifiers } from "@/hooks/useBookQualifiers";
import { QualifiersField } from "@/components/fields/QualifiersField";
import { notifications } from "@/lib/notifications";
import type { Audiobook, AudiobookImage } from "@/types/Audiobook";
import type { MetadataSearchResult } from "@/types/MetadataSearchResult";
import type { LanguageOption } from "@/types/Language";
import type { OrganizeAudiobookInput } from "@/types/OrganizeAudiobookInput";

// DESIGN.md section 1: forms use react-hook-form + zod for client-side validation. The
// server validates independently; this only stops an obviously incomplete submit early.
const bookEditFormSchema = z
  .object({
    authors: z.array(z.string()).min(1, "At least one author is required"),
    narrators: z.array(z.string()),
    bookName: z.string().trim().min(1, "Book title is required"),
    subtitle: z.string(),
    // Recorded split of the title at its colon (see Audiobook.splitTitleOnColon).
    splitTitleOnColon: z.boolean(),
    series: z.string(),
    seriesPart: z.string(),
    // The book's additional (non-primary) series; `series`/`seriesPart` above are the primary one.
    additionalSeries: z.array(z.object({ name: z.string(), part: z.string() })),
    qualifiers: z.array(z.string()),
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
  })
  .superRefine((values, ctx) => {
    // A book relates to a series once: a name repeated (case-insensitively) across the primary and
    // the additional rows is rejected on the later row.
    const seen = new Set<string>();
    const primary = values.series.trim().toLowerCase();
    if (primary) seen.add(primary);
    values.additionalSeries.forEach((row, index) => {
      const name = row.name.trim().toLowerCase();
      if (!name) return;
      if (seen.has(name)) {
        ctx.addIssue({
          code: "custom",
          message: "This series is already listed for the book",
          path: ["additionalSeries", index, "name"],
        });
      }
      seen.add(name);
    });
  });

type BookEditFormValues = z.infer<typeof bookEditFormSchema>;

function valuesFromBook(book: Audiobook): BookEditFormValues {
  return {
    authors: book.authors?.map((a) => a.name) ?? [],
    narrators: book.narrators?.map((n) => n.name) ?? [],
    bookName: book.bookName || "",
    subtitle: book.subtitle || "",
    splitTitleOnColon: book.splitTitleOnColon ?? false,
    series: book.series || "",
    seriesPart: book.seriesPart || "",
    additionalSeries: (book.additionalSeries ?? []).map((r) => ({
      name: r.seriesName,
      part: r.seriesPart ?? "",
    })),
    qualifiers: book.qualifiers ?? [],
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

/**
 * The form's series fields as the book's model: the primary plus the additional ones, blank names
 * dropped. A book whose primary field is empty but has additional series promotes the first of
 * them, so "only one series left" always means it is the primary.
 */
function primaryAndAdditionalSeries(
  values: Pick<BookEditFormValues, "series" | "seriesPart" | "additionalSeries">,
): Pick<Audiobook, "series" | "seriesPart" | "additionalSeries"> {
  const rows = (values.additionalSeries ?? [])
    .map((r) => ({
      seriesName: (r?.name ?? "").trim(),
      seriesPart: (r?.part ?? "").trim() || undefined,
    }))
    .filter((r) => r.seriesName.length > 0);
  const primaryName = values.series?.trim();
  if (primaryName) {
    return {
      series: primaryName,
      seriesPart: values.seriesPart?.trim() || undefined,
      additionalSeries: rows,
    };
  }
  const [promoted, ...rest] = rows;
  return {
    series: promoted?.seriesName,
    seriesPart: promoted?.seriesPart,
    additionalSeries: rest,
  };
}

function buildAudiobook(
  values: BookEditFormValues,
  cover: AudiobookImage | undefined,
  initialBook: Audiobook,
  metadataAppliedFromSearch = false,
  pendingRefreshApplied = false,
  autoSavedFromSearch = false,
): Audiobook {
  return {
    authors: (values.authors ?? []).map((name) => ({ name })),
    narrators: (values.narrators ?? []).map((name) => ({ name })),
    bookName: (values.bookName ?? "").trim(),
    subtitle: values.subtitle?.trim() || undefined,
    splitTitleOnColon: values.splitTitleOnColon,
    ...primaryAndAdditionalSeries(values),
    qualifiers: values.qualifiers ?? [],
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
    metadataAppliedFromSearch,
    pendingRefreshApplied,
    autoSavedFromSearch,
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
  /**
   * A pending metadata-refresh snapshot to review/apply, and the dialog's open state — owned by
   * the caller (BookDetail) so "Refresh Now" can open it. Routing the apply through this form's
   * own TagPreviewDialog (rather than the caller building the saved Audiobook itself) means the
   * apply lands in the mounted form's state and is what gets submitted, so the form displays
   * exactly what was saved instead of going stale after the background save completes and the
   * caller's data refetches.
   */
  pendingRefreshResult?: MetadataSearchResult | null;
  pendingRefreshOpen?: boolean;
  onPendingRefreshOpenChange?: (open: boolean) => void;
  /**
   * The id of the book this form is editing, when there is one. Enable the advisory series-part
   * conflict check (which excludes the current book) once set; the organize/discovered flows,
   * which have no book yet, keep it unset and skip the check. Purely advisory either way: the
   * check never prevents a save.
   */
  currentBookId?: number;
  /**
   * Opens the "Search Online Metadata" dialog once, on mount — set by BookDetail when the search
   * flow was launched from the read-only view page (the view page has no BookSearchDialog of its
   * own; it navigates here and asks the freshly-mounted form to open it).
   */
  autoOpenSearchDialog?: boolean;
  /**
   * Reports whether the form currently has unsaved changes (react-hook-form's own dirty tracking,
   * plus the cover - a separate piece of local state react-hook-form doesn't see). The caller
   * (BookDetail) uses this to show its own "unsaved changes" indicator next to the Done button
   * and to warn before navigating away.
   */
  onDirtyChange?: (dirty: boolean) => void;
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
  pendingRefreshResult,
  pendingRefreshOpen,
  onPendingRefreshOpenChange,
  currentBookId,
  autoOpenSearchDialog = false,
  onDirtyChange,
}: BookEditFormProps) {
  const [cover, setCover] = useState<AudiobookImage | undefined>(initialBook.cover);
  // Mirrors `cover` for synchronous reads. `handleValidSubmit` is invoked via
  // `form.handleSubmit(handleValidSubmit)()` immediately after an auto-submit apply awaits the
  // cover fetch below - but that await only guarantees setCover was *called*, not that React has
  // re-rendered yet, so a closure over the `cover` state variable can still read the pre-apply
  // value. A ref has no such lag: updateCover keeps it in lockstep with every setCover call, and
  // buildAudiobook reads coverRef.current instead of the `cover` variable.
  const coverRef = useRef<AudiobookImage | undefined>(initialBook.cover);
  const updateCover = (next: AudiobookImage | undefined) => {
    coverRef.current = next;
    setCover(next);
  };
  const [newPath, setNewPath] = useState<string | null>(null);
  const [searchDialogOpen, setSearchDialogOpen] = useState(false);
  // Auto-opens once the first time autoOpenSearchDialog is true, not just on mount: BookDetail
  // derives it from the route's search params, which can settle a render or two after this form
  // itself mounts (the router commits the path and the validated search separately) - a
  // mount-only effect would catch it as false and never open the dialog. The ref makes it
  // one-shot regardless: once fired, a later parent re-render (or the value flickering) must not
  // reopen a dialog the user already closed.
  const autoOpenedSearchDialogRef = useRef(false);
  useEffect(() => {
    if (autoOpenSearchDialog && !autoOpenedSearchDialogRef.current) {
      autoOpenedSearchDialogRef.current = true;
      setSearchDialogOpen(true);
    }
  }, [autoOpenSearchDialog]);
  const [saving, setSaving] = useState(false);
  const [showAllOptionalFields, setShowAllOptionalFields] = useState(false);
  // The cover isn't a react-hook-form field, so its own dirty tracking has to compare against the
  // last-known-saved cover directly - state (not just initialBook.cover) so a successful save
  // updates the baseline without needing the caller to pass a fresh initialBook prop back down.
  // Read during render (for isDirty below), so it has to be state rather than a ref.
  const [lastSavedCover, setLastSavedCover] = useState<AudiobookImage | undefined>(
    initialBook.cover,
  );

  const form = useForm<BookEditFormValues>({
    resolver: zodResolver(bookEditFormSchema),
    defaultValues: valuesFromBook(initialBook),
  });

  const { data: languagesRes } = useQuery({
    queryKey: queryKeys.languages(),
    queryFn: () => settingsApi.getLanguages(),
  });
  const languages: LanguageOption[] = languagesRes?.languages ?? [];
  const qualifierOptions = useBookQualifiers();

  const watchedValues = useWatch({ control: form.control });

  const coverIsDirty =
    cover?.base64Data !== lastSavedCover?.base64Data ||
    cover?.mimeType !== lastSavedCover?.mimeType;
  const isDirty = form.formState.isDirty || coverIsDirty;
  useEffect(() => {
    onDirtyChange?.(isDirty);
    // onDirtyChange is expected to be a stable setState-style callback from the caller; only the
    // dirty value itself should retrigger this.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isDirty]);

  // The qualifier set is part of the series-part conflict question (a dramatized Book 2 and a
  // regular Book 2 are different editions, so only books with the same set conflict), and of the
  // suffix suggestion below.
  const qualifiersValue = normalizeQualifiers(watchedValues.qualifiers, qualifierOptions);
  const qualifiersKey = qualifiersValue.join(",");
  const seriesValue = (watchedValues.series ?? "").trim();

  const {
    fields: additionalSeriesFields,
    append: appendAdditionalSeries,
    remove: removeAdditionalSeries,
  } = useFieldArray({ control: form.control, name: "additionalSeries" });

  const addSeries = () => {
    if (!form.getValues("series")?.trim()) {
      // Nothing is the primary yet; the new row would only ever be promoted, so fill the primary.
      form.setFocus("series");
      return;
    }
    appendAdditionalSeries({ name: "", part: "" });
  };

  // Swaps additional series `index` with the primary. An empty primary just promotes the row.
  const makeSeriesPrimary = (index: number) => {
    const row = form.getValues(`additionalSeries.${index}`);
    const primary = {
      name: form.getValues("series") ?? "",
      part: form.getValues("seriesPart") ?? "",
    };
    form.setValue("series", row.name, { shouldDirty: true, shouldValidate: true });
    form.setValue("seriesPart", row.part, { shouldDirty: true });
    if (primary.name.trim()) {
      form.setValue(`additionalSeries.${index}`, primary, { shouldDirty: true });
    } else {
      removeAdditionalSeries(index);
    }
  };

  // Removes the primary series: the first additional one is promoted, or the fields just clear.
  const removePrimarySeries = () => {
    const first = form.getValues("additionalSeries")?.[0];
    if (first) {
      form.setValue("series", first.name, { shouldDirty: true, shouldValidate: true });
      form.setValue("seriesPart", first.part, { shouldDirty: true });
      removeAdditionalSeries(0);
    } else {
      form.setValue("series", "", { shouldDirty: true });
      form.setValue("seriesPart", "", { shouldDirty: true });
    }
  };

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
    const { additionalSeries: watchedAdditional, ...watchedRest } = watchedValues;
    const initialValues = valuesFromBook(initialBook);
    const values: BookEditFormValues = {
      ...initialValues,
      ...watchedRest,
      additionalSeries:
        watchedAdditional?.map((r) => ({ name: r?.name ?? "", part: r?.part ?? "" })) ??
        initialValues.additionalSeries,
    };
    if (!values.bookName?.trim() || !values.authors || values.authors.length === 0) return;

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
  // One-shot signal set once a search result's fields are applied; rides the next save as the
  // metadataAppliedFromSearch marker (see buildAudiobook), then cleared. Reset drops it too.
  // A ref, not state: handleApplyPendingRefresh applies and then submits in the same
  // synchronous handler (auto-submit on Apply All), and a state update wouldn't be visible to
  // handleValidSubmit's closure until the next render - a ref reads the value set moments
  // earlier, in the same tick. The interactive "Search Online Metadata" flow never hit this
  // because Apply and Save are separate clicks with a render in between.
  const metadataAppliedFromSearchRef = useRef(false);
  // Same one-shot signal, for a pending-refresh-snapshot apply specifically: the caller
  // (BookDetail) uses this marker on the saved object to know when to dismiss the stored
  // snapshot, rather than assuming every save with metadataAppliedFromSearch came from there.
  // A ref, not state: handleApplyPendingRefresh sets it and then immediately submits in the
  // same synchronous handler (auto-submit on Apply All), and a state update wouldn't be visible
  // to handleValidSubmit's closure until the next render - a ref reads the value set moments
  // earlier, in the same tick.
  const pendingRefreshAppliedRef = useRef(false);
  // Same one-shot-ref pattern, for the interactive search flow's own auto-save specifically: the
  // caller (BookDetail) reads this marker on the saved object — not a side-channel callback — to
  // know the completing save is this direct-apply flow, so it only rides through to BookDetail if
  // this exact save actually goes out (see BookDetail.proceedSave). A cancelled target-collision
  // dialog or a failed zod validation discards the built object along with the marker, instead of
  // arming an external ref that could outlive this specific save attempt.
  const autoSavedFromSearchRef = useRef(false);

  // A title that already ends in a known qualifier ("Killing Floor (Dramatized)") on a book with
  // no qualifiers set: offer to move the suffix into the qualifier set. Only ever a suggestion -
  // the server never splits a name on its own, since a title can legitimately end that way.
  const suffixSplit = splitQualifiers(
    watchedValues.bookName,
    watchedValues.series,
    qualifierOptions,
  );
  const suffixSuggestion = !qualifiersKey && suffixSplit.qualifiers.length > 0 ? suffixSplit : null;

  // Offer to split "Title: Subtitle" into the two fields (the first ": " is the separator, so
  // "4:50 from Paddington" is never touched). Splitting records the choice on the book, so a later
  // metadata refresh splits the source's title the same way; the checkbox undoes just that
  // record, never the text.
  const titleSplitProposal = splitTitleOnColon(
    watchedValues.bookName ?? "",
    watchedValues.subtitle,
    true,
  );
  const canSplitTitle =
    titleSplitProposal.bookName !== (watchedValues.bookName ?? "") ||
    (titleSplitProposal.subtitle ?? "") !== (watchedValues.subtitle ?? "");

  // The flag is offered whenever the book has a subtitle (so an already-split book can be marked
  // too) and stays visible while set, so it can always be turned back off.
  const showSplitFlag =
    Boolean(watchedValues.splitTitleOnColon) || Boolean(watchedValues.subtitle?.trim());

  const splitTitleNow = () => {
    form.setValue("bookName", titleSplitProposal.bookName, {
      shouldDirty: true,
      shouldValidate: true,
    });
    form.setValue("subtitle", titleSplitProposal.subtitle ?? "", { shouldDirty: true });
    form.setValue("splitTitleOnColon", true, { shouldDirty: true });
  };

  const moveSuffixToQualifiers = () => {
    if (!suffixSuggestion) return;
    form.setValue("bookName", suffixSuggestion.bookName, {
      shouldDirty: true,
      shouldValidate: true,
    });
    form.setValue("series", suffixSuggestion.series ?? "", { shouldDirty: true });
    form.setValue("qualifiers", suffixSuggestion.qualifiers, { shouldDirty: true });
  };

  // Scraped titles carry the suffix ("Killing Floor (Dramatized)") that a stored clean name never
  // does; cleaning it off before the diff means a book that already has the qualifier shows no
  // spurious title/series difference, and a book that does not gets it pre-selected on apply.
  const cleanedPendingSearch = useMemo(
    () => (pendingSearchResult ? cleanSearchResult(pendingSearchResult, qualifierOptions) : null),
    [pendingSearchResult, qualifierOptions],
  );
  const cleanedPendingRefresh = useMemo(
    () => (pendingRefreshResult ? cleanSearchResult(pendingRefreshResult, qualifierOptions) : null),
    [pendingRefreshResult, qualifierOptions],
  );

  const currentOrganizeInput: OrganizeAudiobookInput = useMemo(() => {
    // The series fields exactly as a save would send them (a cleared primary promotes the first
    // additional row), so the tag preview's "current" side matches what buildAudiobook produces.
    const currentSeries = primaryAndAdditionalSeries({
      series: watchedValues.series ?? "",
      seriesPart: watchedValues.seriesPart ?? "",
      additionalSeries: (watchedValues.additionalSeries ??
        []) as BookEditFormValues["additionalSeries"],
    });
    return {
      authors: joinList(watchedValues.authors),
      narrators: joinList(watchedValues.narrators),
      bookName: watchedValues.bookName,
      subtitle: watchedValues.subtitle,
      splitTitleOnColon: watchedValues.splitTitleOnColon,
      series: currentSeries.series,
      seriesPart: currentSeries.seriesPart,
      additionalSeries: currentSeries.additionalSeries,
      year: watchedValues.year ? parseInt(watchedValues.year, 10) : undefined,
      genres: watchedValues.genres?.join("/"),
      description: watchedValues.description,
      copyright: watchedValues.copyright,
      publisher: watchedValues.publisher,
      language: watchedValues.language,
      rating: watchedValues.rating ? Number(watchedValues.rating) : undefined,
      asin: watchedValues.asin,
      www: watchedValues.www,
      qualifiers: watchedValues.qualifiers,
      cover_base64: cover?.base64Data,
      cover_mime: cover?.mimeType,
    };
  }, [watchedValues, cover]);

  const handleSelectSearchResult = (result: MetadataSearchResult) => {
    setPendingSearchResult(result);
    setTagPreviewOpen(true);
  };

  const handleApplyPreviewedTags = async (
    result: MetadataSearchResult,
    selectedFields: Set<string>,
    titleSplit: TitleSplitOutcome = "none",
  ) => {
    if (selectedFields.size === 0) return;
    metadataAppliedFromSearchRef.current = true;
    if (selectedFields.has("bookName") && result.bookName) {
      form.setValue("bookName", result.bookName, { shouldDirty: true });
      // Remember that this title was split, so a metadata refresh splits it the same way - or
      // forget it when the title was applied whole, or the next refresh would re-split it.
      if (titleSplit !== "none") {
        form.setValue("splitTitleOnColon", titleSplit === "split", { shouldDirty: true });
      }
    }
    // Qualifiers the source reported (or whose wording was cleaned off the title) are their own
    // optional field, and only ever add to the book's - taking or skipping the title does not
    // decide them, and one the book already has is never removed.
    if (selectedFields.has("qualifiers") && result.qualifiers?.length) {
      form.setValue(
        "qualifiers",
        normalizeQualifiers(
          [...(form.getValues("qualifiers") ?? []), ...result.qualifiers],
          qualifierOptions,
        ),
        { shouldDirty: true },
      );
    }
    if (selectedFields.has("subtitle")) {
      form.setValue("subtitle", result.subtitle ?? "", { shouldDirty: true });
    }
    if (selectedFields.has("authors") && result.authors) {
      form.setValue(
        "authors",
        result.authors.map((a) => a.name),
        { shouldDirty: true, shouldValidate: true },
      );
    }
    if (selectedFields.has("narrators") && result.narrators) {
      form.setValue(
        "narrators",
        result.narrators.map((n) => n.name),
        { shouldDirty: true },
      );
    }
    if (selectedFields.has("series")) {
      // The dialog hands the series over primary-first (see TagPreviewDialog): the first becomes
      // the book's primary series, the rest its additional ones. A source with no series clears
      // them all, like the server's apply does.
      const [primary, ...others] = result.series ?? [];
      form.setValue("series", primary?.seriesName || "", { shouldDirty: true });
      form.setValue("seriesPart", normalizeSeriesPart(primary?.seriesPart || ""), {
        shouldDirty: true,
      });
      form.setValue(
        "additionalSeries",
        others
          .filter((s) => s.seriesName?.trim())
          .map((s) => ({ name: s.seriesName, part: normalizeSeriesPart(s.seriesPart || "") })),
        { shouldDirty: true, shouldValidate: true },
      );
    }
    if (selectedFields.has("year") && result.year) {
      form.setValue("year", String(result.year), { shouldDirty: true, shouldValidate: true });
    }
    if (selectedFields.has("genres") && result.genres) {
      form.setValue("genres", result.genres, { shouldDirty: true });
    }
    if (selectedFields.has("description")) {
      form.setValue("description", result.description ? cleanDescription(result.description) : "", {
        shouldDirty: true,
      });
    }
    if (selectedFields.has("copyright")) {
      form.setValue("copyright", result.copyright ?? "", { shouldDirty: true });
    }
    if (selectedFields.has("publisher")) {
      form.setValue("publisher", result.publisher ?? "", { shouldDirty: true });
    }
    if (selectedFields.has("language") && result.language) {
      const normalizedLang =
        normalizeLanguage(result.language, languages) ?? result.language.trim();
      form.setValue("language", normalizedLang, { shouldDirty: true });
    }
    if (selectedFields.has("rating")) {
      form.setValue("rating", result.rating != null ? String(result.rating) : "", {
        shouldDirty: true,
      });
    }
    if (selectedFields.has("asin")) {
      form.setValue("asin", result.asin ?? "", { shouldDirty: true });
    }
    if (selectedFields.has("www") && result.cleanUrl) {
      form.setValue("www", result.cleanUrl, { shouldDirty: true });
    }

    const coverUrlToFetch = result.imageUrl;
    if (selectedFields.has("cover") && coverUrlToFetch) {
      // Awaited (not fire-and-forget): callers that auto-submit right after this function
      // returns (the auto-save toggle left off, and the pending-refresh apply) must have the
      // fetched cover in state before buildAudiobook reads it - otherwise the save goes out with
      // the pre-apply cover despite the diff table showing a cover change, with nothing to
      // indicate that to the user. Callers also disable the form (setSaving(true)) before
      // awaiting this, so a slow fetch shows as "saving" rather than a silently unresponsive
      // form - and it cannot hang forever: the proxy has no client timeout of its own, so one is
      // applied here.
      try {
        // lib/api.ts parses every response as JSON; this needs the raw image blob. A GET, so
        // the backend's write guard does not apply to it.
        // eslint-disable-next-line no-restricted-globals -- binary response, see above
        const res = await fetch(
          `/api/metadata-search/proxy-image?url=${encodeURIComponent(coverUrlToFetch)}`,
          { signal: AbortSignal.timeout(15_000) },
        );
        // A non-OK response (e.g. the proxy refusing/failing to reach the source) still has a
        // body - an RFC 9457 problem+json error, not image bytes - and fetch() does not reject
        // for it. Without this check, that error body gets base64-encoded as if it were a valid
        // cover and submitted as one, which the backend's ICoverImageProcessor then rejects with
        // a confusing "not a recognised image format" error instead of the best-effort fallback
        // below actually applying.
        if (!res.ok) {
          throw new Error(`Cover proxy fetch failed with status ${res.status}`);
        }
        const blob = await res.blob();
        const base64Data = await new Promise<string>((resolve, reject) => {
          const reader = new FileReader();
          reader.onloadend = () => {
            const resStr = reader.result as string;
            const idx = resStr.indexOf(";base64,");
            resolve(idx !== -1 ? resStr.substring(idx + 8) : resStr);
          };
          reader.onerror = () => reject(reader.error ?? new Error("Failed to read cover image"));
          reader.readAsDataURL(blob);
        });
        updateCover({ base64Data, mimeType: blob.type || "image/jpeg" });
      } catch {
        // Best-effort: leave the cover as it was rather than blocking the rest of the apply -
        // but the diff table advertised a cover change that silently didn't happen, so surface
        // it rather than letting the user believe the cover was updated.
        notifications.warning(
          "Couldn't fetch the new cover image; the rest of the apply proceeded.",
        );
      }
    }
  };

  const handleCoverUpdate = (base64Data: string | undefined, mimeType: string | undefined) => {
    if (base64Data && mimeType) {
      updateCover({ base64Data, mimeType });
    } else {
      updateCover(undefined);
    }
  };

  const handleValidSubmit = async (values: BookEditFormValues) => {
    setSaving(true);
    // Captured synchronously, before the await below - fields stay editable while the save is
    // in flight (nothing disables the inputs during the request), so reading form.getValues()
    // again *after* the await would pick up an edit made during that window and clear isDirty
    // for a change that was never actually sent. This snapshot is exactly what was submitted.
    const submittedRawValues = form.getValues();
    try {
      await onSave(
        buildAudiobook(
          values,
          coverRef.current,
          initialBook,
          metadataAppliedFromSearchRef.current,
          pendingRefreshAppliedRef.current,
          autoSavedFromSearchRef.current,
        ),
      );
      // All three signals are one-shot: they must ride exactly the save that carried the applied
      // result. A later plain edit of the same book must not re-stamp the refresh timestamp,
      // re-arm the pending-snapshot dismiss flow, or re-arm the return-to-view flow.
      autoSavedFromSearchRef.current = false;
      metadataAppliedFromSearchRef.current = false;
      pendingRefreshAppliedRef.current = false;
      // The just-submitted values are now the saved baseline: reset react-hook-form's dirty
      // tracking against them and move the cover's own baseline forward the same way, so the
      // unsaved-changes indicator and navigation guard clear immediately rather than staying
      // armed against the pre-save values until the caller's data refetches and remounts the
      // form. Reset against the captured raw values, not the zod-resolved `values` - the schema
      // trims bookName/year, so resetting against the trimmed values while the display keeps
      // untrimmed input (e.g. trailing whitespace the user typed) would make isDirty recompute
      // true immediately after a successful save. Same reasoning for coverRef.current over the
      // `cover` state variable: this closure can be stale the same way the pre-fix buildAudiobook
      // call above this block was - coverRef is what's synchronously current.
      form.reset(submittedRawValues, { keepValues: true });
      setLastSavedCover(coverRef.current);
    } finally {
      setSaving(false);
    }
  };

  // Applying a pending metadata-refresh snapshot auto-submits (preserving today's one-click
  // Apply behavior), routed through the form's own submit handler rather than built and saved
  // directly by the caller. This fixes two things at once: the form displays exactly what was
  // saved (no stale mounted defaultValues after the caller's data refetches), and zod validation
  // guards an invalid result (e.g. a cleared authors list) the same way a manual edit would be.
  //
  // Tradeoff: if form.handleSubmit rejects the auto-submit on validation failure, the refs stay
  // armed until a later successful submit. Currently unreachable - no real scraper result can
  // leave the form with zero authors, the only field whose clearing would fail validation.
  //
  // resetSavingOnInvalidSubmit is form.handleSubmit's onInvalid callback: setSaving(true) below
  // disables the form for the whole apply (including the awaited cover fetch, so a slow/hung
  // fetch reads as "saving" instead of a silently unresponsive form, and a manual Save click
  // can't race the auto-submit and fire a second, gate-rejected update). handleValidSubmit's own
  // finally only runs when it is actually invoked - if validation rejects the auto-submit instead,
  // nothing would ever clear `saving` without this.
  const resetSavingOnInvalidSubmit = () => setSaving(false);

  const handleApplyPendingRefresh = async (
    result: MetadataSearchResult,
    selectedFields: Set<string>,
    titleSplit: TitleSplitOutcome,
  ) => {
    if (selectedFields.size === 0) return;
    setSaving(true);
    await handleApplyPreviewedTags(result, selectedFields, titleSplit);
    pendingRefreshAppliedRef.current = true;
    void form.handleSubmit(handleValidSubmit, resetSavingOnInvalidSubmit)();
  };

  // The interactive "Search Online Metadata" flow's own apply step: unlike the pending-refresh
  // snapshot above, this one offers the "don't save automatically" opt-out (TagPreviewDialog's
  // showAutoSaveToggle), so whether it auto-submits depends on that toggle's state at apply time.
  // When it does auto-submit, autoSavedFromSearchRef arms the same client-only marker
  // pendingRefreshAppliedRef already rides on the built Audiobook (buildAudiobook's
  // autoSavedFromSearch) - not a separate callback - so it only reaches BookDetail if this
  // specific save actually goes out (see BookDetail.proceedSave): a cancelled target-collision
  // dialog or a failed zod validation discards the object along with the marker, instead of
  // leaving a side-channel ref armed for whatever save happens to complete next.
  const handleApplySearchResult = async (
    result: MetadataSearchResult,
    selectedFields: Set<string>,
    saveImmediately: boolean,
    titleSplit: TitleSplitOutcome,
  ) => {
    if (selectedFields.size === 0) return;
    if (saveImmediately) setSaving(true);
    await handleApplyPreviewedTags(result, selectedFields, titleSplit);
    if (saveImmediately) {
      autoSavedFromSearchRef.current = true;
      void form.handleSubmit(handleValidSubmit, resetSavingOnInvalidSubmit)();
    }
  };

  const handleReset = () => {
    form.reset(valuesFromBook(initialBook));
    updateCover(initialBook.cover);
    setLastSavedCover(initialBook.cover);
    setShowAllOptionalFields(false);
    metadataAppliedFromSearchRef.current = false;
    pendingRefreshAppliedRef.current = false;
    autoSavedFromSearchRef.current = false;
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
            <Controller
              control={form.control}
              name="authors"
              render={({ field }) => (
                <AuthorsField
                  value={field.value ?? []}
                  onChange={field.onChange}
                  error={form.formState.errors.authors?.message}
                />
              )}
            />

            {isFieldVisible("narrators") && (
              <Controller
                control={form.control}
                name="narrators"
                render={({ field }) => (
                  <NarratorsField value={field.value ?? []} onChange={field.onChange} />
                )}
              />
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

          <Controller
            control={form.control}
            name="qualifiers"
            render={({ field }) => (
              <QualifiersField value={field.value ?? []} onChange={field.onChange} />
            )}
          />

          {qualifiersValue.length > 0 && (
            <p
              className="text-muted-foreground text-xs break-words"
              data-testid="qualifier-preview"
            >
              Saved as:{" "}
              <span className="text-foreground font-medium">
                {applyQualifiers(watchedValues.bookName, qualifiersValue, qualifierOptions)}
              </span>
              {seriesValue && (
                <>
                  {" "}
                  in series{" "}
                  <span className="text-foreground font-medium">
                    {applyQualifiers(seriesValue, qualifiersValue, qualifierOptions)}
                  </span>
                </>
              )}
            </p>
          )}

          {(canSplitTitle || showSplitFlag) && (
            <div
              className="text-muted-foreground flex flex-wrap items-center gap-x-3 gap-y-1 text-xs"
              data-testid="title-split"
            >
              {canSplitTitle && (
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="h-6 px-2 text-xs"
                  onClick={splitTitleNow}
                >
                  Split title at colon
                </Button>
              )}
              {showSplitFlag && (
                <span className="flex items-center gap-2">
                  <Checkbox
                    checked={watchedValues.splitTitleOnColon ?? false}
                    onCheckedChange={(next) =>
                      form.setValue("splitTitleOnColon", next === true, { shouldDirty: true })
                    }
                    aria-label="Split the title at its colon on metadata refresh"
                  />
                  Split the title at its colon on metadata refresh
                </span>
              )}
            </div>
          )}

          {suffixSuggestion && (
            <p
              className="text-status-unknown flex flex-wrap items-center gap-x-2 gap-y-1 text-xs"
              data-testid="qualifier-suggestion"
            >
              <Info className="h-3 w-3 shrink-0" />
              <span className="min-w-0 break-words">
                The title ends in{" "}
                {suffixSuggestion.qualifiers
                  .map((k) => `(${qualifierLabel(k, qualifierOptions)})`)
                  .join(" ")}
                , which is written to disk as a qualifier.
              </span>
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="h-6 px-2 text-xs"
                onClick={moveSuffixToQualifiers}
              >
                Move to qualifiers
              </Button>
            </p>
          )}

          <div className="flex flex-col gap-4 sm:flex-row">
            <Controller
              control={form.control}
              name="series"
              render={({ field }) => (
                <SeriesField
                  value={field.value ?? ""}
                  onChange={field.onChange}
                  ref={field.ref}
                  onBlur={field.onBlur}
                />
              )}
            />

            <div className="min-w-0 flex-1">
              <label className="mb-1 block text-xs font-medium">Series Part / Book #</label>
              <Input {...form.register("seriesPart")} placeholder="e.g. 1 or 2.5" />
            </div>
          </div>

          <SeriesPartHints
            series={watchedValues.series ?? ""}
            part={watchedValues.seriesPart ?? ""}
            qualifiers={qualifiersValue}
            currentBookId={currentBookId}
          />

          {additionalSeriesFields.length > 0 && (
            <div className="flex flex-wrap items-center justify-between gap-2">
              <p className="text-muted-foreground min-w-0 text-xs">
                The series above is the primary one — it is used for the file&apos;s tags and
                location.
              </p>
              <Button type="button" variant="ghost" size="sm" onClick={removePrimarySeries}>
                Remove primary series
              </Button>
            </div>
          )}

          {additionalSeriesFields.map((row, index) => (
            <Controller
              key={row.id}
              control={form.control}
              name={`additionalSeries.${index}`}
              render={({ field, fieldState }) => (
                <SeriesRelationRow
                  position={index + 1}
                  value={field.value}
                  onChange={field.onChange}
                  onMakePrimary={() => makeSeriesPrimary(index)}
                  onRemove={() => removeAdditionalSeries(index)}
                  qualifiers={qualifiersValue}
                  currentBookId={currentBookId}
                  error={
                    form.formState.errors.additionalSeries?.[index]?.name?.message ??
                    fieldState.error?.message
                  }
                />
              )}
            />
          ))}

          <Button type="button" variant="outline" size="sm" onClick={addSeries}>
            <Plus />
            Add series
          </Button>

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

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
            <Controller
              control={form.control}
              name="language"
              render={({ field }) => (
                <LanguageField value={field.value || ""} onChange={field.onChange} />
              )}
            />

            <div>
              <label className="mb-1 block text-xs font-medium">Description</label>
              <Textarea
                {...form.register("description")}
                rows={4}
                placeholder="Book summary or description"
              />
            </div>

            {isFieldVisible("rating") && (
              <div>
                <label className="mb-1 block text-xs font-medium">Rating</label>
                <Input {...form.register("rating")} placeholder="e.g. 4.5" />
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

            {isFieldVisible("asin") && (
              <div>
                <label className="mb-1 block text-xs font-medium">ASIN</label>
                <Input {...form.register("asin")} placeholder="B0..." />
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
          {isDirty && !saving && !isSaving && (
            <span className="flex items-center gap-1 text-xs font-medium text-amber-600 dark:text-amber-400">
              <AlertTriangle className="h-3.5 w-3.5" />
              Unsaved changes
            </span>
          )}
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
        initialQuery={buildDefaultMetadataSearchQuery(
          watchedValues.authors,
          watchedValues.bookName,
          initialBook.fileInfo?.fileName,
        )}
      />

      {cleanedPendingSearch && (
        <TagPreviewDialog
          open={tagPreviewOpen}
          onOpenChange={setTagPreviewOpen}
          currentInput={currentOrganizeInput}
          searchResult={cleanedPendingSearch.result}
          onApply={(result, selectedFields, saveImmediately, titleSplit) => {
            void handleApplySearchResult(result, selectedFields, saveImmediately, titleSplit);
          }}
          showAutoSaveToggle
        />
      )}

      {cleanedPendingRefresh && (
        <TagPreviewDialog
          open={pendingRefreshOpen ?? false}
          onOpenChange={(open) => onPendingRefreshOpenChange?.(open)}
          currentInput={currentOrganizeInput}
          searchResult={cleanedPendingRefresh.result}
          onApply={(result, selectedFields, _saveImmediately, titleSplit) => {
            void handleApplyPendingRefresh(result, selectedFields, titleSplit);
          }}
        />
      )}
    </form>
  );
}

export default BookEditForm;
