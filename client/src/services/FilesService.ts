import BookFileInfo from "../types/BookFileInfo";
import BaseHttpService from "./BaseHttpService";

class FilesService extends BaseHttpService {
  getDirectoryContents(bookPath: string): Promise<BookFileInfo[]> {
    return this.postData("/files/directory_contents", { path: bookPath });
  }

  deleteBook(bookPath: string): Promise<void> {
    return this.postData("/files/delete_directory", { path: bookPath });
  }
}

export default new FilesService();
