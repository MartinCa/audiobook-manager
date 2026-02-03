import { Audiobook } from "../types/Audiobook";
import BaseHttpService from "./BaseHttpService";

function toDto(data: Audiobook) {
  return {
    authors: data.authors.map((a) => a.name),
    narrators: data.narrators.map((n) => n.name),
    bookName: data.bookName,
    subtitle: data.subtitle,
    series: data.series,
    seriesPart: data.seriesPart,
    year: data.year,
    genres: data.genres,
    description: data.description,
    copyright: data.copyright,
    publisher: data.publisher,
    rating: data.rating,
    asin: data.asin,
    www: data.www,
    cover: data.cover,
    filePath: data.fileInfo?.fullPath,
    fileName: data.fileInfo?.fileName,
    sizeInBytes: data.fileInfo?.sizeInBytes ?? 0,
  };
}

class AudiobookService extends BaseHttpService {
  parseBookDetails(bookPath: string): Promise<Audiobook> {
    return this.postData("/audiobook/details", { path: bookPath });
  }

  organizeBook(data: Audiobook): Promise<string> {
    return this.postData("/audiobook/organize", toDto(data));
  }

  generateNewPath(data: Audiobook): Promise<string> {
    return this.postData("/audiobook/generate_path", toDto(data));
  }

  updateBook(id: number, data: Audiobook): Promise<void> {
    return this.putData(`/audiobook/${id}`, toDto(data));
  }
}

export default new AudiobookService();
