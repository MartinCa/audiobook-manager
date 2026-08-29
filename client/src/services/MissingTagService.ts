import { AudiobookMissingTags, MissingTagField } from "../types/MissingTag";
import BaseHttpService from "./BaseHttpService";

class MissingTagService extends BaseHttpService {
  getFields(): Promise<MissingTagField[]> {
    return this.getData("/missing-tags/fields");
  }

  getAudiobooksMissingTags(fields: string[]): Promise<AudiobookMissingTags[]> {
    const query = fields
      .map((f) => `fields=${encodeURIComponent(f)}`)
      .join("&");
    return this.getData(`/missing-tags/audiobooks?${query}`);
  }

  startLanguageBackfill(): Promise<void> {
    return this.postData("/missing-tags/backfill-language");
  }
}

export default new MissingTagService();
