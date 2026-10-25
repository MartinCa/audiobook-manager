import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";
import type { AudiobookPerson } from "./Audiobook";

// AudiobookManager.Scraping/Models/MetadataSearchResult.cs: seriesName is non-nullable.
export type MetadataSeriesSearchResult = Require<
  components["schemas"]["MetadataSeriesSearchResult"],
  "seriesName"
>;

// url/source/authors/narrators/bookName/genres are non-nullable on the class. Series is
// technically nullable on the C# side, but every scraper (Goodreads/Audible/Hardcover)
// unconditionally sets it — never omits it — so it's narrowed to required here too.
export interface MetadataSearchResult {
  url: string;
  cleanUrl: string;
  source: string;
  authors: AudiobookPerson[];
  narrators: AudiobookPerson[];
  bookName: string;
  subtitle?: string;
  duration?: string;
  year?: number;
  language?: string;
  imageUrl?: string;
  series: MetadataSeriesSearchResult[];
  description?: string;
  genres: string[];
  /**
   * What the source's search response said about a description / genres: true or false as
   * reported, null/undefined when that response does not say. A search hit carries neither the
   * description nor the genres themselves, so this is all a results list can show without a
   * details request per hit.
   */
  hasDescription?: boolean | null;
  hasGenres?: boolean | null;
  rating?: number;
  numberOfRatings?: number;
  copyright?: string;
  publisher?: string;
  asin?: string;
  isbn?: string;
  /**
   * Keys of the book qualifiers (abridged, dramatized, ...) the source says this edition carries.
   * The server has already removed their wording from `bookName` (per the configured indicator
   * rules) and sets this; the diff offers them as an additive, optional "Qualifiers" field.
   */
  qualifiers?: string[];
}

export type { MetadataSearchResult as default };
