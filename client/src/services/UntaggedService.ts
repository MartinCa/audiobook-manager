import { Audiobook } from "../types/Audiobook";
import BookFileInfo from "../types/BookFileInfo";
import { PaginatedResult } from "../types/Common";
import BaseHttpService from "./BaseHttpService";

class UntaggedService extends BaseHttpService {
  getUntagged(
    limit: number,
    offset: number,
  ): Promise<PaginatedResult<BookFileInfo>> {
    return this.getData(`/untagged?limit=${limit}&offset=${offset}`);
  }
}

export default new UntaggedService();
