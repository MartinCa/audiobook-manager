<template>
  <v-container>
    <v-row>
      <v-col cols="12">
        <v-btn
          class="mr-3"
          to="/library"
          prepend-icon="mdi-arrow-left"
        >
          Back to Library
        </v-btn>
      </v-col>
    </v-row>

    <v-progress-circular
      v-if="loading"
      indeterminate
      color="primary"
      class="mt-5"
    ></v-progress-circular>

    <template v-if="!loading && bookDetail">
      <v-row class="text-center">
        <v-col class="mb-3">
          <h2 class="headline font-weight-bold">
            {{ bookDetail.authors.join(", ") }} &mdash;
            {{ bookDetail.bookName }}
          </h2>
        </v-col>
      </v-row>

      <v-toolbar>
        <v-btn
          color="primary"
          @click="showSearchDialog = true"
        >
          <v-icon>mdi-magnify</v-icon>
          Search
        </v-btn>
        <v-btn
          color="primary"
          @click="showManualGoodreadsUrlDialog = true"
        >
          <v-icon>mdi-magnify</v-icon>
          Manual Goodreads
        </v-btn>

        <v-spacer></v-spacer>

        <v-btn
          color="primary"
          :disabled="saving"
          @click="saveBook()"
        >
          <template v-if="saving">
            <v-progress-circular
              indeterminate
              size="23"
              :width="2"
            />
          </template>
          <template v-else>
            <v-icon>mdi-content-save</v-icon>
            Save
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
        <v-col class="text-left">
          <div class="text-subtitle-2 mb-1">Path:</div>
          <DiffDisplay
            v-if="newPath"
            :actual="bookDetail.filePath"
            :expected="newPath"
          />
          <span v-else>{{ bookDetail.filePath }}</span>
        </v-col>
      </v-row>

      <v-form ref="form">
        <CoverEditor
          ref="coverEditor"
          :base64-data="input.cover_base64"
          :mime-type="input.cover_mime"
          @update:cover="onCoverUpdate"
        />
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
              :rules="[(v: any) => !!v || 'Authors is required']"
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
              :rules="[(v: any) => !!v || 'Book name is required']"
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
            </v-text-field>
          </v-col>
          <v-col cols="12">
            <v-text-field
              label="Year"
              type="number"
              hide-details="auto"
              density="comfortable"
              :rules="[(v: any) => !!v || 'Year is required']"
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
              :disabled="saving"
              @click="saveBook()"
            >
              <template v-if="saving">
                <v-progress-circular
                  indeterminate
                  size="23"
                  :width="2"
                />
              </template>
              <template v-else>Save</template>
            </v-btn>
          </v-col>
        </v-row>
      </v-form>

      <v-dialog
        v-if="showSearchDialog"
        v-model="showSearchDialog"
        :width="dialogWidth"
        :fullscreen="mdAndDown"
      >
        <BookSearchDialog
          v-if="searchBookDetails"
          :dialog-width="dialogWidth"
          :book-details="searchBookDetails"
          @result-chosen="onSearchResultChosen"
        />
      </v-dialog>
      <v-dialog
        v-if="showManualGoodreadsUrlDialog"
        v-model="showManualGoodreadsUrlDialog"
        :width="dialogWidth"
        :fullscreen="mdAndDown"
      >
        <ManualGoodreadsUrlDialog
          :dialog-width="dialogWidth"
          @result-chosen="onSearchResultChosen"
        />
      </v-dialog>
      <v-dialog
        v-if="showTagPreview"
        v-model="showTagPreview"
        :width="dialogWidth"
        :fullscreen="mdAndDown"
      >
        <TagPreviewDialog
          v-if="pendingSearchResult"
          :dialog-width="dialogWidth"
          :current-input="input"
          :search-result="pendingSearchResult"
          @apply="applyPreviewedTags"
          @cancel="showTagPreview = false"
        />
      </v-dialog>

      <!-- Issues section -->
      <template v-if="bookIssues.length > 0">
        <v-row class="mt-5">
          <v-col cols="12">
            <h3 class="text-h6 mb-3">
              <v-icon
                color="warning"
                class="mr-1"
                >mdi-alert</v-icon
              >
              Issues ({{ bookIssues.length }})
            </h3>
            <v-list density="compact">
              <v-list-item
                v-for="issue in bookIssues"
                :key="issue.id"
              >
                <template v-slot:prepend>
                  <v-icon :icon="getIssueIcon(issue.issueType)" />
                </template>
                <v-list-item-title class="text-wrap">
                  {{ getIssueTypeLabel(issue.issueType) }}
                </v-list-item-title>
                <v-list-item-subtitle class="issue-subtitle text-wrap">
                  <div>{{ issue.description }}</div>
                  <DiffDisplay
                    v-if="issue.expectedValue && issue.actualValue"
                    :expected="issue.expectedValue"
                    :actual="issue.actualValue"
                  />
                  <template v-else>
                    <div
                      v-if="issue.expectedValue"
                      class="text-wrap"
                    >
                      Expected: {{ issue.expectedValue }}
                    </div>
                    <div
                      v-if="issue.actualValue"
                      class="text-wrap"
                    >
                      Actual: {{ issue.actualValue }}
                    </div>
                  </template>
                </v-list-item-subtitle>
                <template v-slot:append>
                  <v-btn
                    size="small"
                    variant="outlined"
                    :loading="resolvingIds.has(issue.id)"
                    @click.stop="resolveIssue(issue)"
                  >
                    Resolve
                  </v-btn>
                </template>
              </v-list-item>
            </v-list>
          </v-col>
        </v-row>
      </template>
    </template>

    <v-snackbar
      v-model="snackbar"
      :timeout="3000"
    >
      {{ snackbarText }}
    </v-snackbar>
  </v-container>
</template>

<script setup lang="ts">
import { computed, onMounted, Ref, ref, watch } from "vue";
import { useRoute } from "vue-router";
import { debounce } from "lodash";
import AudiobookDetail from "../../types/AudiobookDetail";
import OrganizeAudiobookInput from "../../types/OrganizeAudiobookInput";
import { Audiobook, AudiobookImage } from "../../types/Audiobook";
import { BookSearchResult } from "../../types/BookSearchResult";
import ConsistencyIssue from "../../types/ConsistencyIssue";
import BrowseService from "../../services/BrowseService";
import AudiobookService from "../../services/AudiobookService";
import ConsistencyService from "../../services/ConsistencyService";
import BookSearchDialog from "../BookSearchDialog.vue";
import ManualGoodreadsUrlDialog from "../ManualGoodreadsUrlDialog.vue";
import TagPreviewDialog from "../TagPreviewDialog.vue";
import CoverEditor from "../CoverEditor.vue";
import DiffDisplay from "../DiffDisplay.vue";
import { useDialogWidth } from "../dialog";
import { joinPersons } from "../../helpers/bookDetailsHelpers";

const route = useRoute();
const bookId = computed(() => Number(route.params.bookId));

const loading = ref(true);
const saving = ref(false);
const bookDetail: Ref<AudiobookDetail | null> = ref(null);
const form: Ref<any | null> = ref(null);
const input: Ref<OrganizeAudiobookInput> = ref({});
const coverEditor = ref<InstanceType<typeof CoverEditor> | null>(null);
const showSearchDialog = ref(false);
const showManualGoodreadsUrlDialog = ref(false);
const showTagPreview = ref(false);
const pendingSearchResult: Ref<BookSearchResult | null> = ref(null);
const newPath = ref("");

const bookIssues: Ref<ConsistencyIssue[]> = ref([]);
const resolvingIds: Ref<Set<number>> = ref(new Set());
const snackbar = ref(false);
const snackbarText = ref("");

const nonfictionGenre = "Nonfiction";

const { dialogWidth, mdAndDown } = useDialogWidth();

const searchBookDetails = computed((): Audiobook => {
  const bd = bookDetail.value!;
  return {
    authors: bd.authors.map((a) => ({ name: a })) ?? [],
    narrators: bd.narrators.map((n) => ({ name: n })) ?? [],
    bookName: bd.bookName,
    subtitle: bd.subtitle,
    series: bd.series,
    seriesPart: bd.seriesPart,
    year: bd.year,
    genres: bd.genres,
    description: bd.description,
    copyright: bd.copyright,
    publisher: bd.publisher,
    rating: bd.rating,
    asin: bd.asin,
    www: bd.www,
    durationInSeconds: bd.durationInSeconds,
    fileInfo: {
      fullPath: bd.filePath,
      fileName: bd.fileName,
      sizeInBytes: bd.sizeInBytes,
    },
  };
});

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
  (): string[] => input.value.genres?.split("/") ?? [],
);

const isNonfiction = computed((): boolean =>
  genresSplit.value.some((genre) => genre === nonfictionGenre),
);

const resetInput = () => {
  const book = bookDetail.value;
  if (!book) return;
  const rating = book.rating ? Number(book.rating) : undefined;
  input.value = {
    authors: book.authors.join(", "),
    narrators: book.narrators.join(", "),
    bookName: book.bookName,
    subtitle: book.subtitle,
    series: book.series,
    seriesPart: book.seriesPart,
    year: book.year,
    genres: book.genres.join("/"),
    description: book.description,
    copyright: book.copyright,
    publisher: book.publisher,
    asin: book.asin,
    www: book.www,
    rating: rating,
    cover_base64: undefined,
    cover_mime: undefined,
  };
};

const convertInputToAudiobook = (): Audiobook | null => {
  if (!bookDetail.value) return null;

  const inp = input.value;

  let cover: AudiobookImage | undefined = undefined;
  if (inp.cover_base64 && inp.cover_mime) {
    cover = {
      base64Data: inp.cover_base64,
      mimeType: inp.cover_mime,
    };
  }

  return {
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
    durationInSeconds: bookDetail.value.durationInSeconds,
    fileInfo: {
      fullPath: bookDetail.value.filePath,
      fileName: bookDetail.value.fileName,
      sizeInBytes: bookDetail.value.sizeInBytes,
    },
  };
};

const validateForm = async (): Promise<boolean> => {
  if (!form.value) return false;
  const formValidation = await form.value.validate();
  return formValidation.valid;
};

const saveBook = async () => {
  const formValid = await validateForm();
  if (!formValid) return;

  const data = convertInputToAudiobook();
  if (!data) return;

  saving.value = true;
  try {
    await AudiobookService.updateBook(bookId.value, data);
    snackbarText.value = "Book saved successfully";
    snackbar.value = true;
    // Reload detail to reflect changes
    await loadBook();
  } catch (e: any) {
    snackbarText.value = `Failed to save: ${e?.response?.data ?? e.message}`;
    snackbar.value = true;
  } finally {
    saving.value = false;
  }
};

watch(
  input,
  async () => {
    await updateNewBookPath();
  },
  { deep: true },
);

const updateNewBookPath = debounce(async () => {
  const book = convertInputToAudiobook();
  if (book) {
    newPath.value = await AudiobookService.generateNewPath(book);
  }
}, 300);

const onCoverUpdate = (
  base64Data: string | undefined,
  mimeType: string | undefined,
) => {
  input.value.cover_base64 = base64Data;
  input.value.cover_mime = mimeType;
};

const addNonfictionGenre = () => {
  if (isNonfiction.value) return;
  input.value.genres = [...genresSplit.value, nonfictionGenre].join("/");
};

const onSearchResultChosen = (searchData: BookSearchResult | undefined) => {
  showSearchDialog.value = false;
  showManualGoodreadsUrlDialog.value = false;

  if (searchData) {
    pendingSearchResult.value = searchData;
    showTagPreview.value = true;
  }
};

const applyPreviewedTags = (
  result: BookSearchResult,
  selectedFields: Set<string>,
) => {
  showTagPreview.value = false;

  if (selectedFields.has("authors")) {
    input.value.authors = joinPersons(result.authors);
  }
  if (selectedFields.has("narrators")) {
    input.value.narrators = joinPersons(result.narrators) ?? null;
  }
  if (selectedFields.has("bookName")) {
    input.value.bookName = result.bookName;
  }
  if (selectedFields.has("subtitle")) {
    input.value.subtitle = result.subtitle;
  }
  if (selectedFields.has("series")) {
    if (result.series?.length) {
      const seriesData = result.series[0];
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
  }
  if (selectedFields.has("year")) {
    input.value.year = result.year;
  }
  if (selectedFields.has("genres")) {
    input.value.genres = result.genres?.join("/");
  }
  if (selectedFields.has("description")) {
    input.value.description = result.description;
  }
  if (selectedFields.has("rating")) {
    input.value.rating = result.rating;
  }
  if (selectedFields.has("publisher")) {
    input.value.publisher = result.publisher;
  }
  if (selectedFields.has("copyright")) {
    input.value.copyright = result.copyright;
  }
  if (selectedFields.has("asin")) {
    input.value.asin = result.asin;
  }
  if (selectedFields.has("www")) {
    input.value.www = result.url;
  }
  if (selectedFields.has("cover") && result.imageUrl) {
    coverEditor.value?.loadImgFromUrl(result.imageUrl);
  }
};

// Issue helpers
const getIssueIcon = (issueType: string): string => {
  switch (issueType) {
    case "MissingMediaFile":
      return "mdi-file-remove";
    case "WrongFilePath":
      return "mdi-swap-horizontal";
    case "MissingDescTxt":
    case "IncorrectDescTxt":
    case "MissingReaderTxt":
    case "IncorrectReaderTxt":
      return "mdi-text-box-remove";
    case "MissingCoverFile":
      return "mdi-image-remove";
    default:
      return "mdi-alert";
  }
};

const getIssueTypeLabel = (issueType: string): string => {
  switch (issueType) {
    case "MissingMediaFile":
      return "Missing Media File";
    case "WrongFilePath":
      return "Wrong File Path";
    case "MissingDescTxt":
      return "Missing Description File";
    case "IncorrectDescTxt":
      return "Incorrect Description File";
    case "MissingReaderTxt":
      return "Missing Reader File";
    case "IncorrectReaderTxt":
      return "Incorrect Reader File";
    case "MissingCoverFile":
      return "Missing Cover File";
    default:
      return issueType;
  }
};

const resolveIssue = async (issue: ConsistencyIssue) => {
  resolvingIds.value.add(issue.id);
  try {
    await ConsistencyService.resolveIssue(issue.id);
    bookIssues.value = bookIssues.value.filter((i) => {
      if (
        issue.issueType === "MissingMediaFile" ||
        issue.issueType === "WrongFilePath"
      ) {
        return false;
      }
      if (
        issue.issueType === "MissingDescTxt" ||
        issue.issueType === "IncorrectDescTxt" ||
        issue.issueType === "MissingReaderTxt" ||
        issue.issueType === "IncorrectReaderTxt"
      ) {
        return !(
          i.issueType === "MissingDescTxt" ||
          i.issueType === "IncorrectDescTxt" ||
          i.issueType === "MissingReaderTxt" ||
          i.issueType === "IncorrectReaderTxt"
        );
      }
      return i.id !== issue.id;
    });
    snackbarText.value = "Issue resolved successfully";
    snackbar.value = true;
  } catch {
    snackbarText.value = "Failed to resolve issue";
    snackbar.value = true;
  } finally {
    resolvingIds.value.delete(issue.id);
  }
};

const loadBook = async () => {
  loading.value = true;
  try {
    bookDetail.value = await BrowseService.getBookDetail(bookId.value);
    resetInput();
  } finally {
    loading.value = false;
  }
};

const loadIssues = async () => {
  try {
    bookIssues.value = await ConsistencyService.getIssuesByAudiobook(
      bookId.value,
    );
  } catch {
    bookIssues.value = [];
  }
};

onMounted(async () => {
  await Promise.all([loadBook(), loadIssues()]);
});
</script>

<style scoped>
.issue-subtitle {
  white-space: normal !important;
  -webkit-line-clamp: unset !important;
  overflow: visible !important;
}
</style>
