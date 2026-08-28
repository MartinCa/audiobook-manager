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
          <span
            v-if="saving"
            class="text-caption mr-2"
          >
            {{ saveMessage }} ({{ saveProgress }}%)
          </span>
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
            <span
              v-if="saving"
              class="text-caption d-block mb-1"
            >
              {{ saveMessage }} ({{ saveProgress }}%)
            </span>
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
      <v-row class="mt-5">
        <v-col
          cols="12"
          class="d-flex align-center"
        >
          <h3 class="text-h6 mb-0 flex-grow-1">
            <template v-if="bookIssues.length > 0">
              <v-icon
                color="warning"
                class="mr-1"
                >mdi-alert</v-icon
              >
              Issues ({{ bookIssues.length }})
            </template>
            <template v-else>No known issues</template>
          </h3>
          <v-btn
            variant="outlined"
            prepend-icon="mdi-magnify-scan"
            :loading="checking"
            :disabled="saving"
            @click="checkConsistency()"
          >
            Check Consistency
          </v-btn>
        </v-col>
      </v-row>
      <template v-if="bookIssues.length > 0">
        <v-row>
          <v-col cols="12">
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
import { computed, onUnmounted, Ref, ref, watch } from "vue";
import { useRoute } from "vue-router";
import { debounce } from "lodash";
import AudiobookDetail from "../../types/AudiobookDetail";
import OrganizeAudiobookInput from "../../types/OrganizeAudiobookInput";
import { Audiobook } from "../../types/Audiobook";
import ConsistencyIssue from "../../types/ConsistencyIssue";
import BrowseService from "../../services/BrowseService";
import AudiobookService from "../../services/AudiobookService";
import ConsistencyService from "../../services/ConsistencyService";
import BookEditForm from "../BookEditForm.vue";
import DiffDisplay from "../DiffDisplay.vue";
import { convertInputToAudiobook as buildAudiobook } from "../../helpers/organizeAudiobookInput";
import { getIssueIcon } from "../../helpers/consistencyIssueDisplay";
import { HubEventToken, useSignalREvent } from "@/signalr/hub";
import { AudiobookSaveProgress } from "../../signalr/AudiobookSaveProgress";
import { AudiobookSaveComplete } from "../../signalr/AudiobookSaveComplete";
import { AudiobookSaveError } from "../../signalr/AudiobookSaveError";

const AudiobookSaveProgressToken: HubEventToken<AudiobookSaveProgress> =
  "AudiobookSaveProgress";
const AudiobookSaveCompleteToken: HubEventToken<AudiobookSaveComplete> =
  "AudiobookSaveComplete";
const AudiobookSaveErrorToken: HubEventToken<AudiobookSaveError> =
  "AudiobookSaveError";

const apiBaseUrl = import.meta.env.VITE_BASE_API_URL as string;

const route = useRoute();
const bookId = computed(() => Number(route.params.bookId));

const loading = ref(true);
const saving = ref(false);
const saveMessage = ref("");
const saveProgress = ref(0);
const checking = ref(false);
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
    language: bd.language,
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
    language: book.language,
    asin: book.asin,
    www: book.www,
    rating: rating,
    cover_base64: undefined,
    cover_mime: undefined,
  };
};

const convertInputToAudiobook = (): Audiobook | null => {
  if (!bookDetail.value) return null;

  return buildAudiobook(input.value, {
    durationInSeconds: bookDetail.value.durationInSeconds,
    fileInfo: {
      fullPath: bookDetail.value.filePath,
      fileName: bookDetail.value.fileName,
      sizeInBytes: bookDetail.value.sizeInBytes,
    },
  });
};

const saveBook = async () => {
  const formValid = await bookEditForm.value?.validate();
  if (!formValid) return;

  const data = convertInputToAudiobook();
  if (!data) return;

  saving.value = true;
  saveMessage.value = "Started";
  saveProgress.value = 0;
  try {
    // Fire-and-forget: the request only acknowledges the save has started - actual progress,
    // completion, and errors arrive over SignalR (see onSaveProgress/onSaveComplete/onSaveError
    // below), matching the pattern BookOrganize.vue uses for organizing.
    await AudiobookService.updateBook(bookId.value, data);
    bookEditForm.value?.noteSavedNames();
  } catch (e: any) {
    saving.value = false;
    snackbarText.value = `Failed to save: ${e?.response?.data ?? e.message}`;
    snackbar.value = true;
  }
};

const onSaveProgress = (arg: AudiobookSaveProgress) => {
  if (arg.audiobookId !== bookId.value) return;
  saveMessage.value = arg.progressMessage;
  saveProgress.value = arg.progress;
};

const onSaveComplete = async (arg: AudiobookSaveComplete) => {
  if (arg.audiobookId !== bookId.value) return;
  saving.value = false;
  snackbarText.value = "Book saved successfully";
  snackbar.value = true;
  // Reload detail and issues to reflect changes
  await Promise.all([loadBook(), loadIssues()]);
};

const onSaveError = (arg: AudiobookSaveError) => {
  if (arg.audiobookId !== bookId.value) return;
  saving.value = false;
  snackbarText.value = `Failed to save: ${arg.error}`;
  snackbar.value = true;
};

useSignalREvent(AudiobookSaveProgressToken, onSaveProgress);
useSignalREvent(AudiobookSaveCompleteToken, onSaveComplete);
useSignalREvent(AudiobookSaveErrorToken, onSaveError);

// Watches only the fields path generation actually depends on. Deliberately never reads
// cover_base64/cover_mime here: a getter that read them (even to overwrite them afterwards)
// would still track them as reactive dependencies, so editing the cover would keep
// retriggering this debounced call and deep-diffing the large cover string for nothing.
watch(
  () => ({
    authors: input.value.authors,
    narrators: input.value.narrators,
    bookName: input.value.bookName,
    subtitle: input.value.subtitle,
    series: input.value.series,
    seriesPart: input.value.seriesPart,
    year: input.value.year,
    genres: input.value.genres,
    description: input.value.description,
    copyright: input.value.copyright,
    publisher: input.value.publisher,
    asin: input.value.asin,
    www: input.value.www,
    rating: input.value.rating,
  }),
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

// A pending path regeneration would otherwise fire after the component is gone.
onUnmounted(() => {
  updateNewBookPath.cancel();
});

// Issue helpers
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
    case "MissingOpfFile":
      return "Missing OPF File";
    case "IncorrectOpfFile":
      return "Incorrect OPF File";
    case "TagMismatch":
      return "Tag Mismatch";
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

const checkConsistency = async () => {
  checking.value = true;
  try {
    await ConsistencyService.recheckAudiobook(bookId.value);
    await loadIssues();
    snackbarText.value = "Consistency check complete";
    snackbar.value = true;
  } catch {
    snackbarText.value = "Failed to check consistency";
    snackbar.value = true;
  } finally {
    checking.value = false;
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

watch(
  bookId,
  async () => {
    await Promise.all([loadBook(), loadIssues()]);
  },
  { immediate: true },
);
</script>

<style scoped>
.issue-subtitle {
  white-space: normal !important;
  -webkit-line-clamp: unset !important;
  overflow: visible !important;
}
</style>
