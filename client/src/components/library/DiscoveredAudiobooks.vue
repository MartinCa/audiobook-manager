<template>
  <v-container>
    <v-row class="text-center">
      <v-col class="mb-5">
        <h2 class="headline font-weight-bold mb-5">Discovered Audiobooks</h2>
      </v-col>
    </v-row>
    <v-row>
      <v-col cols="12">
        <v-btn
          class="mr-3"
          to="/library"
          prepend-icon="mdi-arrow-left"
        >
          Back to Library
        </v-btn>
        <v-btn
          :disabled="scanning"
          :loading="scanning"
          @click="startScan()"
        >
          Scan Library
        </v-btn>
        <div class="text-caption text-medium-emphasis mt-1">
          Scans the library directory for audiobook files that aren't yet
          tracked in the database.
        </div>
        <template v-if="scanning">
          <v-progress-linear
            class="mt-3"
            :model-value="
              scanTotalFiles > 0 ? (scanFilesScanned / scanTotalFiles) * 100 : 0
            "
            color="primary"
            height="20"
            striped
          >
            <template v-slot:default>
              {{ scanFilesScanned }} / {{ scanTotalFiles }}
            </template>
          </v-progress-linear>
          <div class="text-caption mt-1">{{ scanMessage }}</div>
        </template>
        <v-alert
          v-if="scanComplete"
          type="info"
          class="mt-3"
          closable
          @click:close="scanComplete = false"
        >
          Scan complete: {{ scanNewFiles }} new files discovered,
          {{ scanTrackedFiles }} already tracked.
        </v-alert>
      </v-col>
    </v-row>
    <v-row>
      <v-col cols="12">
        <p class="text-body-2 text-medium-emphasis mb-3">
          Audiobook files found in the library directory that aren't yet tracked
          in the database. Books with a
          <v-chip
            size="x-small"
            color="success"
            class="mx-1"
            >Well tagged</v-chip
          >
          badge already have author, book name, and year and can be added in
          bulk. Expand a book to review its full metadata and add it
          individually.
        </p>
        <v-text-field
          v-model="discoveredSearchQuery"
          label="Filter by filename"
          prepend-inner-icon="mdi-magnify"
          clearable
          hide-details
          density="compact"
          class="mb-3"
        />
        <div
          v-if="discoveredBooks.length"
          class="d-flex align-center justify-space-between mb-2 flex-wrap ga-2"
        >
          <div class="d-flex align-center">
            <v-checkbox
              :model-value="isAllWellTaggedSelected"
              :indeterminate="isSomeWellTaggedSelected"
              :disabled="wellTaggedBooks.length === 0"
              density="compact"
              hide-details
              label="Select all well-tagged"
              @update:model-value="toggleSelectAllWellTagged()"
            />
            <span
              v-if="selectedPaths.size > 0"
              class="text-caption text-medium-emphasis ml-2"
            >
              {{ selectedPaths.size }} selected
            </span>
          </div>
          <v-btn
            v-if="selectedPaths.size > 0"
            size="small"
            variant="outlined"
            color="primary"
            :loading="importing"
            :disabled="importing"
            @click="onImportSelectedClick()"
          >
            Import Selected ({{ selectedPaths.size }})
          </v-btn>
        </div>
        <template v-if="importing">
          <v-progress-linear
            class="mb-3"
            :model-value="
              importTotal > 0 ? (importProcessed / importTotal) * 100 : 0
            "
            color="primary"
            height="20"
            striped
          >
            <template v-slot:default>
              {{ importProcessed }} / {{ importTotal }}
            </template>
          </v-progress-linear>
        </template>
        <v-expansion-panels
          v-if="discoveredBooks.length"
          v-model="discoveredActivePanel"
        >
          <v-expansion-panel
            v-for="(book, i) in discoveredBooks"
            :key="i"
          >
            <v-expansion-panel-title>
              <v-row align="center">
                <v-col cols="auto">
                  <v-checkbox
                    :model-value="selectedPaths.has(book.fullPath)"
                    density="compact"
                    hide-details
                    @click.stop
                    @update:model-value="toggleBookSelected(book)"
                  />
                </v-col>
                <v-col>
                  {{ book.fileName }}
                  <v-chip
                    v-if="book.isWellTagged"
                    size="x-small"
                    color="success"
                    class="ml-2"
                  >
                    Well tagged
                  </v-chip>
                </v-col>
                <v-col>
                  <template v-if="book.error">
                    <span class="text-red">{{ book.error }}</span>
                    <v-icon>mdi-alert</v-icon>
                  </template>
                  <template v-else-if="book.queueId">
                    {{ book.queueMessage ?? "Queued" }}
                    <v-progress-circular
                      :model-value="book.queueProgress ?? 0"
                      size="23"
                      :width="2"
                    />
                  </template>
                </v-col>
                <v-col>
                  {{ formatFileSize(book.sizeInBytes) }}
                </v-col>
              </v-row>
            </v-expansion-panel-title>
            <v-expansion-panel-text>
              <BookOrganize
                :book-path="book.fullPath"
                @book-queued="(id) => markDiscoveredAsQueued(book, id)"
                @book-deleted="() => removeDiscoveredBook(book)"
              />
            </v-expansion-panel-text>
          </v-expansion-panel>
        </v-expansion-panels>
        <div
          v-else
          class="text-center mt-2"
        >
          No discovered audiobooks
        </div>
        <v-pagination
          v-model="discoveredCurrentPage"
          :length="discoveredTotalPages"
        ></v-pagination>
      </v-col>
    </v-row>

    <v-dialog
      v-model="importConfirmDialog"
      max-width="500"
    >
      <v-card>
        <v-card-title>Confirm Import</v-card-title>
        <v-card-text>
          This will add <strong>{{ selectedPaths.size }}</strong> book{{
            selectedPaths.size === 1 ? "" : "s"
          }}
          to the library using the tags already read from each file, moving each
          file into place in the process.
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn @click="importConfirmDialog = false">Cancel</v-btn>
          <v-btn
            color="primary"
            @click="confirmImportSelected()"
          >
            Import
          </v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <v-snackbar
      v-model="snackbar"
      :timeout="4000"
    >
      {{ snackbarText }}
    </v-snackbar>
  </v-container>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, Ref, ref, watch } from "vue";
import { debounce } from "lodash";
import BookOrganize from "../BookOrganize.vue";
import LibraryService from "../../services/LibraryService";
import DiscoveredAudiobook from "../../types/DiscoveredAudiobook";
import { useSignalR, HubEventToken } from "@/signalr/hub";
import { LibraryScanProgress } from "../../signalr/LibraryScanProgress";
import { LibraryScanComplete } from "../../signalr/LibraryScanComplete";
import { ProgressUpdate } from "../../signalr/ProgressUpdate";
import { QueueError } from "../../signalr/QueueError";
import { DiscoveredImportProgress } from "../../signalr/DiscoveredImportProgress";
import { DiscoveredImportComplete } from "../../signalr/DiscoveredImportComplete";

const LibraryScanProgressToken: HubEventToken<LibraryScanProgress> =
  "LibraryScanProgress";
const LibraryScanCompleteToken: HubEventToken<LibraryScanComplete> =
  "LibraryScanComplete";
const UpdateProgress: HubEventToken<ProgressUpdate> = "UpdateProgress";
const QueueErrorToken: HubEventToken<QueueError> = "QueueError";
const DiscoveredImportProgressToken: HubEventToken<DiscoveredImportProgress> =
  "DiscoveredImportProgress";
const DiscoveredImportCompleteToken: HubEventToken<DiscoveredImportComplete> =
  "DiscoveredImportComplete";

const signalR = useSignalR();

const limit = 50;

const discoveredBooks: Ref<DiscoveredAudiobook[]> = ref([]);
const discoveredSearchQuery: Ref<string> = ref("");
const discoveredActivePanel: Ref<any> = ref(null);
const discoveredCurrentPage: Ref<number> = ref(1);
const discoveredTotalItems: Ref<number> = ref(0);

const selectedPaths: Ref<Set<string>> = ref(new Set());
const importing: Ref<boolean> = ref(false);
const importProcessed: Ref<number> = ref(0);
const importTotal: Ref<number> = ref(0);
const importConfirmDialog: Ref<boolean> = ref(false);
const snackbar: Ref<boolean> = ref(false);
const snackbarText: Ref<string> = ref("");

const scanning: Ref<boolean> = ref(false);
const scanMessage: Ref<string> = ref("");
const scanFilesScanned: Ref<number> = ref(0);
const scanTotalFiles: Ref<number> = ref(0);
const scanComplete: Ref<boolean> = ref(false);
const scanNewFiles: Ref<number> = ref(0);
const scanTrackedFiles: Ref<number> = ref(0);

const onLibraryScanProgress = (arg: LibraryScanProgress) => {
  scanning.value = true;
  scanMessage.value = arg.message;
  scanFilesScanned.value = arg.filesScanned;
  scanTotalFiles.value = arg.totalFiles;
};

const onLibraryScanComplete = (arg: LibraryScanComplete) => {
  scanning.value = false;
  scanComplete.value = true;
  scanNewFiles.value = arg.newFilesDiscovered;
  scanTrackedFiles.value = arg.alreadyTracked;
  loadDiscoveredBooks();
};

const onUpdateProgress = (arg: ProgressUpdate) => {
  const book = discoveredBooks.value.find(
    (x) => x.queueId === arg.originalFileLocation,
  );
  if (book) {
    book.queueMessage = arg.progressMessage;
    book.queueProgress = arg.progress;
  }
};

const onQueueError = (arg: QueueError) => {
  const book = discoveredBooks.value.find(
    (x) => x.queueId === arg.originalFileLocation,
  );
  if (book) {
    book.error = arg.error;
  }
};

const onDiscoveredImportProgress = (arg: DiscoveredImportProgress) => {
  importProcessed.value = arg.processed;
  importTotal.value = arg.total;
};

const onDiscoveredImportComplete = (arg: DiscoveredImportComplete) => {
  importing.value = false;
  let msg = `Import complete: ${arg.totalSucceeded} of ${arg.totalProcessed} books added`;
  if (arg.totalFailed > 0) {
    msg += ` (${arg.totalFailed} failed)`;
  }
  snackbarText.value = msg;
  snackbar.value = true;
  selectedPaths.value = new Set();
  loadDiscoveredBooks();
};

signalR.on(LibraryScanProgressToken, onLibraryScanProgress);
signalR.on(LibraryScanCompleteToken, onLibraryScanComplete);
signalR.on(UpdateProgress, onUpdateProgress);
signalR.on(QueueErrorToken, onQueueError);
signalR.on(DiscoveredImportProgressToken, onDiscoveredImportProgress);
signalR.on(DiscoveredImportCompleteToken, onDiscoveredImportComplete);

onUnmounted(() => {
  signalR.off(LibraryScanProgressToken, onLibraryScanProgress);
  signalR.off(LibraryScanCompleteToken, onLibraryScanComplete);
  signalR.off(UpdateProgress, onUpdateProgress);
  signalR.off(QueueErrorToken, onQueueError);
  signalR.off(DiscoveredImportProgressToken, onDiscoveredImportProgress);
  signalR.off(DiscoveredImportCompleteToken, onDiscoveredImportComplete);
});

const discoveredTotalPages = computed((): number =>
  Math.ceil(discoveredTotalItems.value / limit),
);

const wellTaggedBooks = computed((): DiscoveredAudiobook[] =>
  discoveredBooks.value.filter((b) => b.isWellTagged),
);

const isAllWellTaggedSelected = computed(
  (): boolean =>
    wellTaggedBooks.value.length > 0 &&
    wellTaggedBooks.value.every((b) => selectedPaths.value.has(b.fullPath)),
);

const isSomeWellTaggedSelected = computed(
  (): boolean =>
    !isAllWellTaggedSelected.value &&
    wellTaggedBooks.value.some((b) => selectedPaths.value.has(b.fullPath)),
);

const toggleSelectAllWellTagged = () => {
  if (isAllWellTaggedSelected.value) {
    for (const book of wellTaggedBooks.value) {
      selectedPaths.value.delete(book.fullPath);
    }
  } else {
    for (const book of wellTaggedBooks.value) {
      selectedPaths.value.add(book.fullPath);
    }
  }
  selectedPaths.value = new Set(selectedPaths.value);
};

const toggleBookSelected = (book: DiscoveredAudiobook) => {
  if (selectedPaths.value.has(book.fullPath)) {
    selectedPaths.value.delete(book.fullPath);
  } else {
    selectedPaths.value.add(book.fullPath);
  }
  selectedPaths.value = new Set(selectedPaths.value);
};

const onImportSelectedClick = () => {
  importConfirmDialog.value = true;
};

const confirmImportSelected = async () => {
  importConfirmDialog.value = false;
  importing.value = true;
  importProcessed.value = 0;
  importTotal.value = 0;
  try {
    await LibraryService.bulkImportDiscovered(Array.from(selectedPaths.value));
  } catch (e: any) {
    importing.value = false;
    snackbarText.value = `Failed to start import: ${e?.response?.data ?? e.message}`;
    snackbar.value = true;
  }
};

watch(discoveredCurrentPage, () => {
  loadDiscoveredBooks();
});

const startScan = async () => {
  scanning.value = true;
  scanComplete.value = false;
  scanFilesScanned.value = 0;
  scanTotalFiles.value = 0;
  scanMessage.value = "";
  await LibraryService.startLibraryScan();
};

const loadDiscoveredBooks = async () => {
  const result = await LibraryService.getDiscoveredBooks(
    limit,
    (discoveredCurrentPage.value - 1) * limit,
    discoveredSearchQuery.value || undefined,
  );
  discoveredTotalItems.value = result.total;
  discoveredBooks.value = result.items;
};

const debouncedDiscoveredSearch = debounce(() => {
  discoveredCurrentPage.value = 1;
  loadDiscoveredBooks();
}, 300);

watch(discoveredSearchQuery, () => {
  debouncedDiscoveredSearch();
});

const markDiscoveredAsQueued = async (
  book: DiscoveredAudiobook,
  queueId: string,
) => {
  book.queueId = queueId;
  var bookIdx = discoveredBooks.value.indexOf(book);
  if (bookIdx === discoveredActivePanel.value) {
    discoveredActivePanel.value = null;
  }
  selectedPaths.value.delete(book.fullPath);
  selectedPaths.value = new Set(selectedPaths.value);
  await LibraryService.deleteDiscoveredBook(book.fullPath);
};

const removeDiscoveredBook = (book: DiscoveredAudiobook) => {
  var bookIdx = discoveredBooks.value.indexOf(book);
  var currentlyOpen = bookIdx === discoveredActivePanel.value;

  discoveredBooks.value = discoveredBooks.value.filter((b) => b != book);
  selectedPaths.value.delete(book.fullPath);
  selectedPaths.value = new Set(selectedPaths.value);

  if (currentlyOpen) {
    discoveredActivePanel.value = null;
  }
};

const formatFileSize = (size: number) => {
  const sizeInMb = size / 1000000;
  return `${sizeInMb.toFixed(1)} MB`;
};

onMounted(() => {
  loadDiscoveredBooks();
});
</script>
