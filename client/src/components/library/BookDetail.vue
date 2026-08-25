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

      <BookEditForm
        ref="bookEditForm"
        v-model:input="input"
        :search-book-details="searchBookDetails"
        :current-path="bookDetail.filePath"
        :new-path="newPath"
        :cover-url="coverUrl"
        @reset="resetInput"
      >
        <template #toolbar-actions>
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
        </template>
        <template #form-actions>
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
        </template>
      </BookEditForm>

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
import ConsistencyIssue from "../../types/ConsistencyIssue";
import BrowseService from "../../services/BrowseService";
import AudiobookService from "../../services/AudiobookService";
import ConsistencyService from "../../services/ConsistencyService";
import BookEditForm from "../BookEditForm.vue";
import DiffDisplay from "../DiffDisplay.vue";

const apiBaseUrl = import.meta.env.VITE_BASE_API_URL as string;

const route = useRoute();
const bookId = computed(() => Number(route.params.bookId));

const loading = ref(true);
const saving = ref(false);
const bookDetail: Ref<AudiobookDetail | null> = ref(null);
const bookEditForm = ref<InstanceType<typeof BookEditForm> | null>(null);
const input: Ref<OrganizeAudiobookInput> = ref({});
const newPath = ref("");

const bookIssues: Ref<ConsistencyIssue[]> = ref([]);
const resolvingIds: Ref<Set<number>> = ref(new Set());
const snackbar = ref(false);
const snackbarText = ref("");

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

const coverUrl = computed((): string | undefined =>
  bookDetail.value?.coverFilePath
    ? `${apiBaseUrl}/browse/audiobooks/${bookDetail.value.id}/cover`
    : undefined,
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

const saveBook = async () => {
  const formValid = await bookEditForm.value?.validate();
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
    try {
      newPath.value = await AudiobookService.generateNewPath(book);
    } catch {
      newPath.value = "";
    }
  }
}, 300);

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
    await Promise.all([loadBook(), loadIssues()]);
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
