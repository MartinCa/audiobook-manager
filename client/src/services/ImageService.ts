import axios from "axios";
import { AudiobookImage } from "../types/Audiobook";

class ImageService {
  downloadImageFromUrl(imageUrl: string): Promise<AudiobookImage> {
    return new Promise<AudiobookImage>((resolve, reject) => {
      axios.get(imageUrl, {
        responseType: "arraybuffer"
      }).then((response) => {
        const contentType = response.headers["content-type"];
        if (contentType == undefined) {
          reject("Invalid image response");
          return;
        }
        // const base64 = response.data.toString("base64");
        const base64 = btoa(String.fromCharCode.apply(null, [...new Uint8Array(response.data)]));
        resolve({
          base64Data: base64,
          mimeType: contentType
        });
      }).catch((reason) => {
        reject(reason);
      })
    })
  }
}

export default new ImageService();
