import axios from "axios";
import { AudiobookImage } from "../types/Audiobook";

const base64Regex = new RegExp(/^data.*,/);

class ImageService {
  downloadImageFromUrl(imageUrl: string): Promise<AudiobookImage> {
    return new Promise<AudiobookImage>((resolve, reject) => {
      axios
        .get(imageUrl, {
          responseType: "arraybuffer",
        })
        .then((response) => {
          const contentType = response.headers["content-type"];
          if (contentType == undefined) {
            reject("Invalid image response");
            return;
          }
          let blob = new Blob([response.data]);
          return this.readBase64ImageFromBlob(blob);
        })
        .catch((reason) => {
          reject(reason);
        });
    });
  }

  readBase64ImageFromBlob(
    imageBlob: Blob,
    mimeType?: string
  ): Promise<AudiobookImage> {
    return new Promise((resolve, reject) => {
      const contentType = mimeType ?? imageBlob.type;

      let reader = new FileReader();
      reader.onload = (event) => {
        if (typeof event.target?.result === "string") {
          resolve({
            base64Data: event.target?.result.replace(base64Regex, ""),
            mimeType: contentType,
          });
        } else {
          reject("Could not read image");
        }
      };

      reader.onerror = (ev) => {
        reject(ev);
      };

      reader.readAsDataURL(imageBlob);
    });
  }
}

export default new ImageService();
