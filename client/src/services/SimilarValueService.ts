import { SimilarValueGroup } from "../types/SimilarValue";
import BaseHttpService from "./BaseHttpService";

const NAME_CACHE_TTL_MS = 5 * 60 * 1000;

interface NameCacheEntry {
  fetchedAt: number;
  promise: Promise<string[]>;
}

class SimilarValueService extends BaseHttpService {
  private authorNamesCache: NameCacheEntry | null = null;
  private seriesNamesCache: NameCacheEntry | null = null;

  getSimilarAuthors(): Promise<SimilarValueGroup[]> {
    return this.getData("/similar-values/similar-authors");
  }

  getSimilarSeries(): Promise<SimilarValueGroup[]> {
    return this.getData("/similar-values/similar-series");
  }

  startAlign(
    valueType: "author" | "series",
    sourceValues: string[],
    targetValue: string,
  ): Promise<void> {
    return this.postData("/similar-values/align", {
      valueType,
      sourceValues,
      targetValue,
    });
  }

  getAuthorNames(): Promise<string[]> {
    if (this.isCacheValid(this.authorNamesCache)) {
      return this.authorNamesCache!.promise;
    }
    const promise = this.getData<string[]>("/similar-values/author-names");
    this.authorNamesCache = { fetchedAt: Date.now(), promise };
    return promise;
  }

  getSeriesNames(): Promise<string[]> {
    if (this.isCacheValid(this.seriesNamesCache)) {
      return this.seriesNamesCache!.promise;
    }
    const promise = this.getData<string[]>("/similar-values/series-names");
    this.seriesNamesCache = { fetchedAt: Date.now(), promise };
    return promise;
  }

  invalidateNameCaches(): void {
    this.authorNamesCache = null;
    this.seriesNamesCache = null;
  }

  /**
   * Merges newly-saved names into the cached lists in place, without a server round-trip.
   * Keeps autocomplete/hints up to date across a session of many saves; the cache still
   * expires normally via its TTL, at which point it's refetched from the server as usual.
   */
  addKnownAuthorNames(names: string[]): void {
    this.mergeIntoCache("authorNamesCache", names);
  }

  addKnownSeriesNames(names: string[]): void {
    this.mergeIntoCache("seriesNamesCache", names);
  }

  private mergeIntoCache(
    cacheField: "authorNamesCache" | "seriesNamesCache",
    names: string[],
  ): void {
    const cache = this[cacheField];
    if (!cache || names.length === 0) {
      return;
    }
    cache.promise = cache.promise.then((existing) => {
      const merged = new Set(existing);
      for (const name of names) {
        if (name) {
          merged.add(name);
        }
      }
      return Array.from(merged).sort((a, b) => a.localeCompare(b));
    });
  }

  private isCacheValid(entry: NameCacheEntry | null): boolean {
    return !!entry && Date.now() - entry.fetchedAt < NAME_CACHE_TTL_MS;
  }
}

export default new SimilarValueService();
