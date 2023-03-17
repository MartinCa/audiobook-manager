import { Audiobook } from "../types/Audiobook";
import BaseHttpService from "./BaseHttpService";

class AudiobookService extends BaseHttpService {
  parseBookDetails(bookPath: string): Promise<Audiobook> {
    return this.postData("/audiobook/details", { path: bookPath });
  }

  organizeBook(data: Audiobook): Promise<string> {
    return this.postData("/audiobook/organize", data);
  }

  generateNewPath(data: Audiobook): Promise<string> {
    return this.postData("/audiobook/generate_path", data);
  }
}

export default new AudiobookService();
