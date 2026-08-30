import React, { useState } from "react";
import { Search, Save, Trash2, Folder } from "lucide-react";
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
import CoverEditor from "./CoverEditor";
import BookSearchDialog from "./BookSearchDialog";
import { DiscoveredAudiobook, MetadataSearchResult } from "@/types/domain";
import { joinList, splitList } from "@/helpers/organizeAudiobookInput";

interface BookEditFormProps {
  initialBook: DiscoveredAudiobook;
  languages: { code: string; name: string }[];
  onSave: (payload: any) => Promise<void>;
  onDelete?: (fullPath: string) => Promise<void>;
}

export const BookEditForm: React.FC<BookEditFormProps> = ({
  initialBook,
  languages,
  onSave,
  onDelete,
}) => {
  const [bookName, setBookName] = useState(initialBook.bookName || "");
  const [subtitle] = useState<string | undefined>(undefined);
  const [authors, setAuthors] = useState(joinList(initialBook.authors));
  const [narrators, setNarrators] = useState(joinList(initialBook.narrators));
  const [series, setSeries] = useState(initialBook.series || "");
  const [seriesPart, setSeriesPart] = useState(initialBook.seriesPart || "");
  const [year, setYear] = useState<string>(
    initialBook.year ? String(initialBook.year) : "",
  );
  const [genres, setGenres] = useState(joinList(initialBook.genres));
  const [description, setDescription] = useState(initialBook.description || "");
  const [copyright] = useState(initialBook.copyright || "");
  const [publisher] = useState(initialBook.publisher || "");
  const [rating] = useState(initialBook.rating || "");
  const [asin] = useState(initialBook.asin || "");
  const [www] = useState(initialBook.www || "");
  const [language, setLanguage] = useState(initialBook.language || "");
  const [base64Cover, setBase64Cover] = useState<string | undefined>(undefined);
  const [searchDialogOpen, setSearchDialogOpen] = useState(false);
  const [saving, setSaving] = useState(false);

  const handleApplySearchResult = (result: MetadataSearchResult) => {
    if (result.title) setBookName(result.title);
    if (result.authors && result.authors.length > 0)
      setAuthors(joinList(result.authors));
    if (result.narrators && result.narrators.length > 0)
      setNarrators(joinList(result.narrators));
    if (result.series) setSeries(result.series);
    if (result.seriesPart) setSeriesPart(result.seriesPart);
    if (result.year) setYear(String(result.year));
    if (result.genres && result.genres.length > 0)
      setGenres(joinList(result.genres));
    if (result.description) setDescription(result.description);
    if (result.language) setLanguage(result.language);
    if (result.coverUrl) setBase64Cover(result.coverUrl);
    setSearchDialogOpen(false);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    try {
      await onSave({
        fullPath: initialBook.fullPath,
        bookName,
        subtitle: subtitle || undefined,
        authors: splitList(authors),
        narrators: splitList(narrators),
        series: series || undefined,
        seriesPart: seriesPart || undefined,
        year: year ? Number(year) : undefined,
        genres: splitList(genres),
        description: description || undefined,
        copyright: copyright || undefined,
        publisher: publisher || undefined,
        rating: rating || undefined,
        asin: asin || undefined,
        www: www || undefined,
        language: language || undefined,
        base64Cover,
      });
    } finally {
      setSaving(false);
    }
  };

  return (
    <form
      onSubmit={handleSubmit}
      className="space-y-6"
    >
      <div className="flex justify-between items-center">
        <div className="flex items-center space-x-2 text-xs text-muted-foreground truncate">
          <Folder className="h-4 w-4 shrink-0" />
          <span className="truncate">{initialBook.fullPath}</span>
        </div>
        <div className="flex gap-2">
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => setSearchDialogOpen(true)}
          >
            <Search className="h-4 w-4 mr-2" />
            Scrape Metadata
          </Button>
          {onDelete && (
            <Button
              type="button"
              variant="destructive"
              size="sm"
              onClick={() => onDelete(initialBook.fullPath)}
            >
              <Trash2 className="h-4 w-4 mr-2" />
              Delete File
            </Button>
          )}
        </div>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        <div className="md:col-span-1 flex flex-col items-center">
          <CoverEditor onCoverChange={setBase64Cover} />
        </div>

        <div className="md:col-span-2 space-y-4">
          <div className="grid grid-cols-1 gap-4">
            <div>
              <label className="text-xs font-semibold">Title</label>
              <Input
                value={bookName}
                onChange={(e) => setBookName(e.target.value)}
                required
              />
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="text-xs font-semibold">
                  Author(s) (slash separated)
                </label>
                <Input
                  value={authors}
                  onChange={(e) => setAuthors(e.target.value)}
                />
              </div>
              <div>
                <label className="text-xs font-semibold">
                  Narrator(s) (slash separated)
                </label>
                <Input
                  value={narrators}
                  onChange={(e) => setNarrators(e.target.value)}
                />
              </div>
            </div>

            <div className="grid grid-cols-3 gap-4">
              <div>
                <label className="text-xs font-semibold">Series</label>
                <Input
                  value={series}
                  onChange={(e) => setSeries(e.target.value)}
                />
              </div>
              <div>
                <label className="text-xs font-semibold">Series Part</label>
                <Input
                  value={seriesPart}
                  onChange={(e) => setSeriesPart(e.target.value)}
                />
              </div>
              <div>
                <label className="text-xs font-semibold">Year</label>
                <Input
                  type="number"
                  value={year}
                  onChange={(e) => setYear(e.target.value)}
                />
              </div>
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="text-xs font-semibold">
                  Genres (slash separated)
                </label>
                <Input
                  value={genres}
                  onChange={(e) => setGenres(e.target.value)}
                />
              </div>
              <div>
                <label className="text-xs font-semibold">Language</label>
                <Select
                  value={language}
                  onValueChange={setLanguage}
                >
                  <SelectTrigger>
                    <SelectValue placeholder="Select Language" />
                  </SelectTrigger>
                  <SelectContent>
                    {languages.map((l) => (
                      <SelectItem
                        key={l.code}
                        value={l.code}
                      >
                        {l.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </div>

            <div>
              <label className="text-xs font-semibold">Description</label>
              <Textarea
                rows={4}
                value={description}
                onChange={(e: React.ChangeEvent<HTMLTextAreaElement>) =>
                  setDescription(e.target.value)
                }
              />
            </div>
          </div>
        </div>
      </div>

      <div className="flex justify-end pt-4 border-t border-border">
        <Button
          type="submit"
          disabled={saving}
        >
          <Save className="h-4 w-4 mr-2" />
          {saving ? "Organizing..." : "Organize & Save"}
        </Button>
      </div>

      <BookSearchDialog
        open={searchDialogOpen}
        onOpenChange={setSearchDialogOpen}
        onSelectResult={handleApplySearchResult}
      />
    </form>
  );
};
export default BookEditForm;
