import axios from "axios";
import { AudiobookImage } from "../types/Audiobook";

const base64Regex = new RegExp(/^data[^,]+,/);

class ImageService {
  private downloadImageBlob(
    imageUrl: string,
  ): Promise<{ blob: Blob; contentType: string }> {
    return new Promise<{ blob: Blob; contentType: string }>(
      (resolve, reject) => {
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
            resolve({ blob, contentType });
          })
          .catch((reason) => {
            reject(reason);
          });
      },
    );
  }

  async downloadImageFromUrl(imageUrl: string): Promise<AudiobookImage> {
    var imageBlob = await this.downloadImageBlob(imageUrl);
    return this.readBase64ImageFromBlob(imageBlob.blob, imageBlob.contentType);
  }

  readBase64ImageFromBlob(
    imageBlob: Blob,
    mimeType?: string,
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
