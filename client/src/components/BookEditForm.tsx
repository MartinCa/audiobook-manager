import { useState, useEffect, useCallback, useMemo, type ReactNode } from "react";
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
import type { Audiobook, AudiobookImage } from "@/types/Audiobook";
import type { MetadataSearchResult } from "@/types/MetadataSearchResult";
import type { LanguageOption } from "@/types/Language";
import type { OrganizeAudiobookInput } from "@/types/OrganizeAudiobookInput";

export interface BookEditFormProps {
  initialBook: Audiobook;
  currentPath?: string;
  coverUrl?: string;
  onSave: (book: Audiobook) => void | Promise<void>;
  onReset?: () => void;
  toolbarActions?: ReactNode;
  formActions?: ReactNode;
}

export function BookEditForm({
  initialBook,
  currentPath,
  coverUrl,
  onSave,
  onReset,
  toolbarActions,
  formActions,
}: BookEditFormProps) {
  const [bookName, setBookName] = useState(initialBook.bookName || "");
  const [subtitle, setSubtitle] = useState(initialBook.subtitle || "");
  const [authors, setAuthors] = useState(joinList(initialBook.authors?.map((a) => a.name)));
  const [narrators, setNarrators] = useState(joinList(initialBook.narrators?.map((n) => n.name)));
  const [series, setSeries] = useState(initialBook.series || "");
  const [seriesPart, setSeriesPart] = useState(initialBook.seriesPart || "");
  const [year, setYear] = useState(initialBook.year ? String(initialBook.year) : "");
  const [genres, setGenres] = useState((initialBook.genres || []).join(" / "));
  const [description, setDescription] = useState(initialBook.description || "");
  const [copyright, setCopyright] = useState(initialBook.copyright || "");
  const [publisher, setPublisher] = useState(initialBook.publisher || "");
  const [language, setLanguage] = useState(initialBook.language || "");
  const [rating, setRating] = useState(initialBook.rating || "");
  const [asin, setAsin] = useState(initialBook.asin || "");
  const [www, setWww] = useState(initialBook.www || "");
  const [cover, setCover] = useState<AudiobookImage | undefined>(initialBook.cover);

  const [newPath, setNewPath] = useState<string | null>(null);
  const [searchDialogOpen, setSearchDialogOpen] = useState(false);
  const [saving, setSaving] = useState(false);

  const { data: languagesRes } = useQuery({
    queryKey: ["languages"],
    queryFn: () => settingsApi.getLanguages(),
  });
  const languages: LanguageOption[] = languagesRes?.languages ?? [];

  const buildAudiobook = useCallback((): Audiobook => {
    const authorList = splitList(authors).map((name) => ({ name }));
    const narratorList = splitList(narrators).map((name) => ({ name }));
    const genreList = genres
      .split("/")
      .map((g) => g.trim())
      .filter(Boolean);

    return {
      authors: authorList,
      narrators: narratorList,
      bookName: bookName.trim(),
      subtitle: subtitle.trim() || undefined,
      series: series.trim() || undefined,
      seriesPart: seriesPart.trim() || undefined,
      year: year ? parseInt(year, 10) : undefined,
      genres: genreList,
      description: description.trim() || undefined,
      copyright: copyright.trim() || undefined,
      publisher: publisher.trim() || undefined,
      language: language.trim() || undefined,
      rating: rating.trim() || undefined,
      asin: asin.trim() || undefined,
      www: www.trim() || undefined,
      cover,
      fileInfo: initialBook.fileInfo,
      durationInSeconds: initialBook.durationInSeconds,
    };
  }, [
    authors,
    narrators,
    genres,
    bookName,
    subtitle,
    series,
    seriesPart,
    year,
    description,
    copyright,
    publisher,
    language,
    rating,
    asin,
    www,
    cover,
    initialBook.fileInfo,
    initialBook.durationInSeconds,
  ]);

  useEffect(() => {
    if (bookName && authors) {
      const book = buildAudiobook();
      const timer = setTimeout(() => {
        void audiobookApi
          .generateNewPath(book)
          .then((generated) => {
            setNewPath(generated);
          })
          .catch(() => {});
      }, 300);
      return () => clearTimeout(timer);
    }
  }, [
    bookName,
    subtitle,
    authors,
    narrators,
    series,
    seriesPart,
    year,
    genres,
    description,
    copyright,
    publisher,
    language,
    rating,
    asin,
    www,
    cover,
    initialBook,
    buildAudiobook,
  ]);

  const [tagPreviewOpen, setTagPreviewOpen] = useState(false);
  const [pendingSearchResult, setPendingSearchResult] = useState<MetadataSearchResult | null>(null);

  const currentOrganizeInput: OrganizeAudiobookInput = useMemo(
    () => ({
      authors,
      narrators,
      bookName,
      subtitle,
      series,
      seriesPart,
      year: year ? parseInt(year, 10) : undefined,
      genres,
      description,
      copyright,
      publisher,
      language,
      rating: rating ? Number(rating) : undefined,
      asin,
      www,
      cover_base64: cover?.base64Data,
      cover_mime: cover?.mimeType,
    }),
    [
      authors,
      narrators,
      bookName,
      subtitle,
      series,
      seriesPart,
      year,
      genres,
      description,
      copyright,
      publisher,
      language,
      rating,
      asin,
      www,
      cover,
    ],
  );

  const handleSelectSearchResult = (result: MetadataSearchResult) => {
    setPendingSearchResult(result);
    setTagPreviewOpen(true);
  };

  const handleApplyPreviewedTags = (result: MetadataSearchResult, selectedFields: Set<string>) => {
    if (selectedFields.has("bookName") && result.bookName) setBookName(result.bookName);
    if (selectedFields.has("subtitle") && result.subtitle) setSubtitle(result.subtitle);
    if (selectedFields.has("authors") && result.authors && result.authors.length > 0) {
      setAuthors(joinList(result.authors.map((a) => a.name)));
    }
    if (selectedFields.has("narrators") && result.narrators && result.narrators.length > 0) {
      setNarrators(joinList(result.narrators.map((n) => n.name)));
    }
    if (selectedFields.has("series")) {
      const firstSeries = result.series?.[0];
      const sName = firstSeries?.seriesName;
      const sPart = firstSeries?.seriesPart;
      if (sName !== undefined) setSeries(sName || "");
      if (sPart !== undefined) setSeriesPart(normalizeSeriesPart(sPart || ""));
    }
    if (selectedFields.has("year") && result.year) setYear(String(result.year));
    if (selectedFields.has("genres") && result.genres && result.genres.length > 0) {
      setGenres(result.genres.join(" / "));
    }
    if (selectedFields.has("description") && result.description) {
      setDescription(cleanDescription(result.description));
    }
    if (selectedFields.has("copyright") && result.copyright) setCopyright(result.copyright);
    if (selectedFields.has("publisher") && result.publisher) setPublisher(result.publisher);
    if (selectedFields.has("language") && result.language) setLanguage(result.language);
    if (selectedFields.has("rating") && result.rating) setRating(String(result.rating));
    if (selectedFields.has("asin") && result.asin) setAsin(result.asin);
    if (selectedFields.has("www") && result.url) {
      setWww(result.url);
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

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    try {
      await onSave(buildAudiobook());
    } finally {
      setSaving(false);
    }
  };

  const handleReset = () => {
    setAuthors(joinList(initialBook.authors.map((a) => a.name)));
    setNarrators(joinList(initialBook.narrators.map((n) => n.name)));
    setBookName(initialBook.bookName || "");
    setSubtitle(initialBook.subtitle || "");
    setSeries(initialBook.series || "");
    setSeriesPart(initialBook.seriesPart || "");
    setYear(initialBook.year ? String(initialBook.year) : "");
    setGenres(initialBook.genres.join(" / "));
    setDescription(initialBook.description || "");
    setCopyright(initialBook.copyright || "");
    setPublisher(initialBook.publisher || "");
    setLanguage(initialBook.language || "");
    setRating(initialBook.rating || "");
    setAsin(initialBook.asin || "");
    setWww(initialBook.www || "");
    setCover(initialBook.cover);
    onReset?.();
  };

  return (
    <form
      onSubmit={(e) => {
        void handleSubmit(e);
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
                value={authors}
                onChange={(e) => setAuthors(e.target.value)}
                placeholder="Author Name, Second Author"
                required
              />
            </div>

            <div>
              <label className="mb-1 block text-xs font-medium">Narrators</label>
              <Input
                value={narrators}
                onChange={(e) => setNarrators(e.target.value)}
                placeholder="Narrator Name"
              />
            </div>

            <div>
              <label className="mb-1 block text-xs font-medium">
                Book Title <span className="text-destructive">*</span>
              </label>
              <Input
                value={bookName}
                onChange={(e) => setBookName(e.target.value)}
                placeholder="Book title"
                required
              />
            </div>

            <div>
              <label className="mb-1 block text-xs font-medium">Subtitle</label>
              <Input
                value={subtitle}
                onChange={(e) => setSubtitle(e.target.value)}
                placeholder="Subtitle"
              />
            </div>

            <div>
              <label className="mb-1 block text-xs font-medium">Series</label>
              <Input
                value={series}
                onChange={(e) => setSeries(e.target.value)}
                placeholder="Series name"
              />
            </div>

            <div>
              <label className="mb-1 block text-xs font-medium">Series Part / Book #</label>
              <Input
                value={seriesPart}
                onChange={(e) => setSeriesPart(e.target.value)}
                placeholder="e.g. 1 or 2.5"
              />
            </div>

            <div>
              <label className="mb-1 block text-xs font-medium">
                Year <span className="text-destructive">*</span>
              </label>
              <Input
                type="number"
                value={year}
                onChange={(e) => setYear(e.target.value)}
                placeholder="YYYY"
                required
              />
            </div>

            <div>
              <label className="mb-1 block text-xs font-medium">Genres (separated by /)</label>
              <Input
                value={genres}
                onChange={(e) => setGenres(e.target.value)}
                placeholder="Fantasy / Fiction"
              />
            </div>
          </div>

          <div>
            <label className="mb-1 block text-xs font-medium">Description</label>
            <Textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={4}
              placeholder="Book summary or description"
            />
          </div>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <div>
              <label className="mb-1 block text-xs font-medium">Language</label>
              <Select value={language} onValueChange={setLanguage}>
                <SelectTrigger>
                  <SelectValue placeholder="Select language..." />
                </SelectTrigger>
                <SelectContent>
                  {languages.map((l) => (
                    <SelectItem key={l.code} value={l.code}>
                      {l.displayName}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div>
              <label className="mb-1 block text-xs font-medium">Publisher</label>
              <Input
                value={publisher}
                onChange={(e) => setPublisher(e.target.value)}
                placeholder="Publisher"
              />
            </div>

            <div>
              <label className="mb-1 block text-xs font-medium">Copyright</label>
              <Input
                value={copyright}
                onChange={(e) => setCopyright(e.target.value)}
                placeholder="Copyright year / owner"
              />
            </div>

            <div>
              <label className="mb-1 block text-xs font-medium">Rating</label>
              <Input
                value={rating}
                onChange={(e) => setRating(e.target.value)}
                placeholder="e.g. 4.5"
              />
            </div>

            <div>
              <label className="mb-1 block text-xs font-medium">ASIN</label>
              <Input value={asin} onChange={(e) => setAsin(e.target.value)} placeholder="B0..." />
            </div>

            <div>
              <label className="mb-1 flex items-center justify-between text-xs font-medium">
                <span>Web link / URL</span>
                {www && (
                  <a
                    href={www}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="text-primary flex items-center hover:underline"
                  >
                    <ExternalLink className="mr-0.5 h-3 w-3" /> Preview
                  </a>
                )}
              </label>
              <Input
                value={www}
                onChange={(e) => setWww(e.target.value)}
                placeholder="https://..."
              />
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
        initialQuery={bookName || initialBook.fileInfo?.fileName || ""}
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
