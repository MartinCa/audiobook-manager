<template>
  <ErrorNotifications
    :errors="errors"
    @error-dismissed="onErrorDismissed"
  />

  <v-progress-circular
    v-if="!bookDetails"
    indeterminate
    color="primary"
  ></v-progress-circular>
  <template v-else>
    <v-toolbar>
      <v-btn
        color="primary"
        @click="showSearchDialog = true"
      >
        <v-icon>mdi-magnify</v-icon>
        Search
      </v-btn>

      <v-spacer></v-spacer>

      <v-btn
        color="primary"
        :disabled="organizing"
        @click="organizeBook(true)"
      >
        <template v-if="organizing">
          <v-progress-circular
            indeterminate
            size="23"
            :width="2"
          />
        </template>
        <template v-else>
          <v-icon>mdi-book-plus</v-icon>
          Organize
        </template>
      </v-btn>

      <v-btn
        :href="goodreadsQuery"
        target="_blank"
      >
        <v-icon>mdi-magnify</v-icon>
        Goodreads
      </v-btn>
    </v-toolbar>
    <v-row>
      <v-col class="text-left"> Current path: {{ bookPath }} </v-col>
    </v-row>
    <v-row>
      <v-col class="text-left"> Organized path: {{ newPath }} </v-col>
    </v-row>
    <v-form ref="form">
      <v-row>
        <v-col
          cols="12"
          md="6"
          lg="3"
        >
          <v-img
            v-if="input.cover_base64"
            max-height="200"
            class="bg-grey-darken-2"
            transition="false"
            :src="`data:${input.cover_mime};base64,${input.cover_base64}`"
          ></v-img>
          <template v-else> No cover </template>
        </v-col>
        <v-col
          cols="12"
          md="6"
          lg="9"
        >
          <v-row>
            <v-col
              cols="12"
              md="9"
            >
              <v-text-field
                label="Image url"
                hide-details="auto"
                v-model="imgUrl"
              ></v-text-field>
            </v-col>
            <v-col
              cols="12"
              md="3"
            >
              <v-btn
                color="primary"
                size="large"
                block
                @click="loadImgFromUrl(imgUrl)"
              >
                Fetch
              </v-btn>
            </v-col>
          </v-row>
          <v-row>
            <v-col
              cols="12"
              md="9"
            >
              <v-file-input
                label="Cover image upload"
                hide-details="auto"
                accept="image/*"
                v-model="uploadedImg"
              ></v-file-input>
            </v-col>
            <v-col
              cols="12"
              md="3"
            >
              <v-btn
                color="primary"
                size="large"
                block
                @click="loadUploadedImg(uploadedImg)"
              >
                Upload
              </v-btn>
            </v-col>
          </v-row>
        </v-col>
      </v-row>
      <v-row>
        <v-col
          cols="12"
          md="6"
        >
          <v-text-field
            label="Authors"
            hide-details="auto"
            hint="Separated by ','"
            density="comfortable"
            :rules="[(v) => !!v || 'Authors is required']"
            v-model="input.authors"
          ></v-text-field>
        </v-col>
        <v-col
          cols="12"
          md="6"
        >
          <v-text-field
            label="Narrators"
            hide-details="auto"
            density="comfortable"
            hint="Separated by ','"
            v-model="input.narrators"
          ></v-text-field>
        </v-col>
        <v-col
          cols="12"
          md="6"
        >
          <v-text-field
            label="Book name"
            hide-details="auto"
            density="comfortable"
            :rules="[(v) => !!v || 'Book name is required']"
            v-model="input.bookName"
          ></v-text-field>
        </v-col>
        <v-col
          cols="12"
          md="6"
        >
          <v-text-field
            label="Subtitle"
            hide-details="auto"
            density="comfortable"
            v-model="input.subtitle"
          ></v-text-field>
        </v-col>
        <v-col
          cols="12"
          sm="6"
        >
          <v-text-field
            label="Series name"
            hide-details="auto"
            density="comfortable"
            v-model="input.series"
          >
            <template
              v-slot:prepend
              v-if="seriesMappedNamed"
            >
              <v-icon :title="seriesMappedNamed"> mdi-information </v-icon>
            </template>
          </v-text-field>
        </v-col>
        <v-col
          cols="12"
          sm="6"
        >
          <v-text-field
            label="Series part"
            hide-details="auto"
            density="comfortable"
            v-model="input.seriesPart"
          >
            <template
              v-slot:prepend
              v-if="input.seriesPartWarning"
            >
              <v-icon title="Series part might not be correct">
                mdi-alert
              </v-icon>
            </template>
          </v-text-field>
        </v-col>
        <v-col cols="12">
          <v-text-field
            label="Year"
            type="number"
            hide-details="auto"
            density="comfortable"
            :rules="[(v) => !!v || 'Year is required']"
            v-model="input.year"
          ></v-text-field>
        </v-col>
        <v-col
          cols="12"
          sm="8"
          md="9"
          lg="10"
        >
          <v-text-field
            label="Genres"
            hint="Separated by '/'"
            hide-details="auto"
            density="comfortable"
            v-model="input.genres"
          >
          </v-text-field>
        </v-col>
        <v-col
          cols="12"
          sm="4"
          md="3"
          lg="2"
        >
          <v-btn
            color="primary"
            size="large"
            :disabled="isNonfiction"
            block
            @click="addNonfictionGenre"
          >
            Add Nonfiction
          </v-btn>
        </v-col>
        <v-col cols="12">
          <v-textarea
            label="Description"
            hide-details="auto"
            density="comfortable"
            v-model="input.description"
          >
          </v-textarea>
        </v-col>
        <v-col
          cols="12"
          sm="6"
        >
          <v-text-field
            label="Copyright"
            hide-details="auto"
            density="comfortable"
            v-model="input.copyright"
          >
          </v-text-field>
        </v-col>
        <v-col
          cols="12"
          sm="6"
        >
          <v-text-field
            label="Publisher"
            hide-details="auto"
            density="comfortable"
            v-model="input.publisher"
          >
          </v-text-field>
        </v-col>

        <v-col
          cols="12"
          sm="6"
          class="text-left"
        >
          <v-text-field
            label="Www"
            hide-details="auto"
            density="comfortable"
            v-model="input.www"
          >
          </v-text-field>
          <a
            v-if="input.www"
            :href="input.www"
            target="_blank"
            >Preview</a
          >
        </v-col>
        <v-col
          cols="12"
          sm="6"
        >
          <v-text-field
            label="Rating"
            type="number"
            hide-details="auto"
            density="comfortable"
            v-model="input.rating"
          >
          </v-text-field>
        </v-col>
      </v-row>
      <v-row>
        <v-col
          cols="12"
          sm="4"
        >
          <v-btn
            color="warning"
            @click="resetInput()"
          >
            Reset input
          </v-btn>
        </v-col>
        <v-col
          cols="12"
          sm="4"
        >
          <v-btn
            color="primary"
            :disabled="organizing"
            @click="organizeBook(true)"
          >
            <template v-if="organizing">
              <v-progress-circular
                indeterminate
                size="23"
                :width="2"
              />
            </template>
            <template v-else>Organize</template>
          </v-btn>
        </v-col>
        <v-col
          cols="12"
          sm="4"
        >
          <v-btn
            color="error"
            @click="showDeleteDialog = true"
          >
            Delete
          </v-btn>
        </v-col>
      </v-row>
    </v-form>
    <v-dialog
      v-model="showSearchDialog"
      :width="dialogWidth"
      :fullscreen="mdAndDown"
    >
      <BookSearchDialog
        v-if="showSearchDialog"
        :dialog-width="dialogWidth"
        :book-details="bookDetails"
        @result-chosen="readSearchResult"
      />
    </v-dialog>
    <v-dialog
      v-model="showDeleteDialog"
      :width="dialogWidth"
      :fullscreen="mdAndDown"
    >
      <BookDeleteDialog
        v-if="showDeleteDialog"
        :dialog-width="dialogWidth"
        :book-details="bookDetails"
        @delete-book="removeBook"
      />
    </v-dialog>
  </template>
</template>

<script setup lang="ts">
import { computed, onMounted, Ref, ref, watch } from "vue";
import { Audiobook, AudiobookImage } from "../types/Audiobook";
import OrganizeAudiobookInput from "../types/OrganizeAudiobookInput";
import BookSearchDialog from "./BookSearchDialog.vue";
import BookDeleteDialog from "./BookDeleteDialog.vue";
import { BookSearchResult } from "../types/BookSearchResult";
import ImageService from "../services/ImageService";
import ErrorNotifications from "./ErrorNotifications.vue";
import { useDialogWidth } from "./dialog";
import { useErrors } from "./errors";
import AudiobookService from "../services/AudiobookService";
import { joinPersons } from "../helpers/bookDetailsHelpers";
import { debounce, update } from "lodash";

const props = defineProps<{
  bookPath: string;
}>();

const emit = defineEmits<{
  (e: "bookDeleted"): void;
  (e: "bookQueued", id: string): void;
}>();

const bookDetails: Ref<Audiobook | null> = ref(null);
const form: Ref<any | null> = ref(null);
const input: Ref<OrganizeAudiobookInput> = ref({});
const imgUrl = ref("");
const showSearchDialog = ref(false);
const organizing = ref(false);
const showDeleteDialog = ref(false);
const newPath = ref("");
const uploadedImg = ref([]);

const nonfictionGenre = "Nonfiction";

const goodreadsQuery = computed((): string => {
  let queryTokens: string[] = [];
  if (input.value.authors) {
    queryTokens = queryTokens.concat(input.value.authors?.split(" "));
  }
  if (input.value.bookName) {
    queryTokens = queryTokens.concat(input.value.bookName?.split(" "));
  }
  const query = queryTokens.join("+");
  return `https://www.goodreads.com/search?utf8=%E2%9C%93&search_type=books&search[query]=${query}`;
});

const genresSplit = computed(
  (): string[] => input.value.genres?.split("/") ?? []
);

const isNonfiction = computed((): boolean =>
  genresSplit.value.some((genre) => genre === nonfictionGenre)
);

const seriesMappedNamed = computed((): string => {
  if (
    !input.value.seriesOriginal ||
    input.value.seriesOriginal == input.value.series
  ) {
    return "";
  }
  return `Series name was mapped from '${input.value.seriesOriginal}'`;
});

const { dialogWidth, mdAndDown } = useDialogWidth();

watch(
  input,
  async (newValue, oldValue) => {
    await updateNewBookPath();
  },
  { deep: true }
);

const updateNewBookPath = debounce(async () => {
  var book = convertInputToAudiobook();
  if (book) {
    newPath.value = await AudiobookService.generateNewPath(book);
  }
}, 300);

const resetInput = () => {
  const book = bookDetails.value;
  const rating = book?.rating ? Number(book?.rating) : undefined;
  input.value = {
    authors: book?.authors.map((x) => x.name).join(", "),
    narrators: book?.narrators.map((x) => x.name).join(", "),
    bookName: book?.bookName,
    subtitle: book?.subtitle,
    series: book?.series,
    seriesPart: book?.seriesPart,
    year: book?.year,
    genres: book?.genres.join("/"),
    description: book?.description,
    copyright: book?.copyright,
    publisher: book?.publisher,
    asin: book?.asin,
    www: book?.www,
    rating: rating,
    cover_base64: bookDetails.value?.cover?.base64Data,
    cover_mime: bookDetails.value?.cover?.mimeType,
  };
};
const convertInputToAudiobook = (): Audiobook | null => {
  if (!bookDetails.value) {
    return null;
  }

  const inp = input.value;

  let cover: AudiobookImage | undefined = undefined;
  if (inp.cover_base64 && inp.cover_mime) {
    cover = {
      base64Data: inp.cover_base64,
      mimeType: inp.cover_mime,
    };
  }

  let newBook: Audiobook = {
    authors: inp.authors?.split(",").map((x) => ({ name: x.trim() })) ?? [],
    narrators: inp.narrators?.split(",").map((x) => ({ name: x.trim() })) ?? [],
    bookName: inp.bookName,
    subtitle: inp.subtitle,
    series: inp.series,
    seriesPart: inp.seriesPart,
    year: inp.year,
    genres: inp.genres?.split("/") ?? [],
    description: inp.description,
    copyright: inp.copyright,
    publisher: inp.publisher,
    rating: inp.rating?.toString(),
    asin: inp.asin,
    www: inp.www,
    cover: cover,
    durationInSeconds: bookDetails.value.durationInSeconds,
    fileInfo: bookDetails.value.fileInfo,
  };

  return newBook;
};

const validateForm = async (): Promise<boolean> => {
  if (!form) {
    return false;
  }

  const formValidation = await form.value.validate();

  return formValidation.valid;
};

const organizeBook = async (relocate = false) => {
  const formValid = await validateForm();

  if (!formValid) {
    return;
  }

  const data = convertInputToAudiobook();
  if (!data) {
    // TODO: Show error
    return;
  }

  organizing.value = true;

  const organizeId = await AudiobookService.organizeBook(data);
  emit("bookQueued", organizeId);
};
const getBookDetails = async () => {
  const book = await AudiobookService.parseBookDetails(props.bookPath);
  bookDetails.value = book;
  resetInput();
};

const loadUploadedImg = async (uploaded: File[]) => {
  var cover = await ImageService.readBase64ImageFromBlob(uploaded[0]);
  input.value.cover_base64 = cover.base64Data;
  input.value.cover_mime = cover.mimeType;
};

const addNonfictionGenre = () => {
  if (isNonfiction.value) {
    return;
  }

  input.value.genres = [...genresSplit.value, nonfictionGenre].join("/");
};

const loadImgFromUrl = async (overwriteUrl: string | undefined) => {
  if (!overwriteUrl) {
    input.value.cover_base64 = undefined;
    input.value.cover_mime = undefined;
    return;
  }
  const coverUrl = overwriteUrl;
  const cover = await ImageService.downloadImageFromUrl(coverUrl);
  input.value.cover_base64 = cover.base64Data;
  input.value.cover_mime = cover.mimeType;
};
const readSearchResult = (searchData: BookSearchResult | undefined) => {
  if (searchData) {
    input.value.authors = joinPersons(searchData.authors);
    input.value.narrators = joinPersons(searchData.narrators) ?? null;
    input.value.www = searchData.url;
    input.value.bookName = searchData.bookName;
    input.value.subtitle = searchData.subtitle;
    if (searchData.series?.length) {
      const seriesData = searchData.series[0];
      input.value.series = seriesData.seriesName;
      input.value.seriesOriginal = seriesData.originalSeriesName;
      input.value.seriesPart = seriesData.seriesPart;
      input.value.seriesPartWarning = seriesData.partWarning;
    } else {
      input.value.series = "";
      input.value.seriesOriginal = "";
      input.value.seriesPart = "";
      input.value.seriesPartWarning = false;
    }
    input.value.year = searchData.year;
    input.value.genres = searchData.genres?.join("/");
    input.value.description = searchData.description;
    input.value.rating = searchData.rating;
    input.value.publisher = searchData.publisher;
    input.value.copyright = searchData.copyright;
    input.value.asin = searchData.asin;
    loadImgFromUrl(searchData.imageUrl);
  }

  showSearchDialog.value = false;
};
const removeBook = (remove: boolean) => {
  if (remove) {
    emit("bookDeleted");
  }

  showDeleteDialog.value = false;
};
onMounted(() => {
  getBookDetails();
});

const { errors, onErrorDismissed } = useErrors();
</script>
