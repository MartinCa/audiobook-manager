import { LanguageOptions } from "../types/Language";
import BaseHttpService from "./BaseHttpService";

/**
 * The supported languages come from the backend rather than a list held here, for the same
 * reason the metadata source list does: two copies of the same fixed list silently drift, and
 * the one the user sees is the one that is wrong. The list never changes within a session, so
 * the in-flight promise itself is the cache — concurrent callers on first mount share one
 * request instead of racing several.
 */
class LanguageService extends BaseHttpService {
  private cache: Promise<LanguageOptions> | null = null;

  getLanguageOptions(): Promise<LanguageOptions> {
    if (!this.cache) {
      this.cache = this.getData<LanguageOptions>("/settings/languages").catch(
        (err) => {
          // Don't cache a failure: a request that failed because the API was briefly
          // unreachable would otherwise leave every later caller with the same rejection.
          this.cache = null;
          throw err;
        },
      );
    }
    return this.cache;
  }

  invalidateCache(): void {
    this.cache = null;
  }
}

export default new LanguageService();
