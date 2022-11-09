import { Audiobook } from "../types/Audiobook";
import BaseHttpService from "./BaseHttpService";


class AudiobookService extends BaseHttpService {
  parseBookDetails(bookPath: string): Promise<Audiobook> {
    return this.postData("/audiobook/details", { path: bookPath });
  }

  organizeBook(data: Audiobook): Promise<Audiobook> {
    return this.postData("/audiobook/organize", data);
  }
}

export default new AudiobookService();
