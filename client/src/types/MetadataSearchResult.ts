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
  rating?: number;
  numberOfRatings?: number;
  copyright?: string;
  publisher?: string;
  asin?: string;
  isbn?: string;
}

export type { MetadataSearchResult as default };
