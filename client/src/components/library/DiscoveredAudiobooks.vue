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
          in the database. Expand a book to review its metadata and add it.
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
        <v-expansion-panels
          v-if="discoveredBooks.length"
          v-model="discoveredActivePanel"
        >
          <v-expansion-panel
            v-for="(book, i) in discoveredBooks"
            :key="i"
          >
            <v-expansion-panel-title>
              <v-row>
                <v-col>
                  {{ book.fileName }}
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
  </v-container>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, Ref, ref, watch } from "vue";
import { debounce } from "lodash";
import BookOrganize from "../BookOrganize.vue";
import LibraryService from "../../services/LibraryService";
import BookFileInfo from "../../types/BookFileInfo";
import { useSignalR, HubEventToken } from "@/signalr/hub";
import { LibraryScanProgress } from "../../signalr/LibraryScanProgress";
import { LibraryScanComplete } from "../../signalr/LibraryScanComplete";
import { ProgressUpdate } from "../../signalr/ProgressUpdate";
import { QueueError } from "../../signalr/QueueError";

const LibraryScanProgressToken: HubEventToken<LibraryScanProgress> =
  "LibraryScanProgress";
const LibraryScanCompleteToken: HubEventToken<LibraryScanComplete> =
  "LibraryScanComplete";
const UpdateProgress: HubEventToken<ProgressUpdate> = "UpdateProgress";
const QueueErrorToken: HubEventToken<QueueError> = "QueueError";

const signalR = useSignalR();

const limit = 50;

const discoveredBooks: Ref<BookFileInfo[]> = ref([]);
const discoveredSearchQuery: Ref<string> = ref("");
const discoveredActivePanel: Ref<any> = ref(null);
const discoveredCurrentPage: Ref<number> = ref(1);
const discoveredTotalItems: Ref<number> = ref(0);

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

signalR.on(LibraryScanProgressToken, onLibraryScanProgress);
signalR.on(LibraryScanCompleteToken, onLibraryScanComplete);
signalR.on(UpdateProgress, onUpdateProgress);
signalR.on(QueueErrorToken, onQueueError);

onUnmounted(() => {
  signalR.off(LibraryScanProgressToken, onLibraryScanProgress);
  signalR.off(LibraryScanCompleteToken, onLibraryScanComplete);
  signalR.off(UpdateProgress, onUpdateProgress);
  signalR.off(QueueErrorToken, onQueueError);
});

const discoveredTotalPages = computed((): number =>
  Math.ceil(discoveredTotalItems.value / limit),
);

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

const markDiscoveredAsQueued = async (book: BookFileInfo, queueId: string) => {
  book.queueId = queueId;
  var bookIdx = discoveredBooks.value.indexOf(book);
  if (bookIdx === discoveredActivePanel.value) {
    discoveredActivePanel.value = null;
  }
  await LibraryService.deleteDiscoveredBook(book.fullPath);
};

const removeDiscoveredBook = (book: BookFileInfo) => {
  var bookIdx = discoveredBooks.value.indexOf(book);
  var currentlyOpen = bookIdx === discoveredActivePanel.value;

  discoveredBooks.value = discoveredBooks.value.filter((b) => b != book);

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
