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
          <OperationProgressBar
            class="mt-3"
            :processed="scanFilesScanned"
            :total="scanTotalFiles"
          />
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
          individually. Books marked
          <v-chip
            size="x-small"
            color="warning"
            class="mx-1"
            >Duplicate</v-chip
          >
          already have a file at their target location and are excluded from
          bulk import - expand them to compare and resolve.
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
          <OperationProgressBar
            class="mb-3"
            :processed="importProcessed"
            :total="importTotal"
          />
        </template>
        <v-expansion-panels
          v-if="discoveredBooks.length"
          v-model="discoveredActivePanel"
        >
          <v-expansion-panel
            v-for="book in discoveredBooks"
            :key="book.fullPath"
            :value="book.fullPath"
          >
            <v-expansion-panel-title>
              <v-row align="center">
                <v-col cols="auto">
                  <v-checkbox
                    :model-value="selectedPaths.has(book.fullPath)"
                    :disabled="book.isDuplicate"
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
                  <v-chip
                    v-if="book.isDuplicate"
                    size="x-small"
                    color="warning"
                    class="ml-2"
                  >
                    Duplicate - expand to resolve
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
import OperationProgressBar from "../OperationProgressBar.vue";
import LibraryService from "../../services/LibraryService";
import DiscoveredAudiobook from "../../types/DiscoveredAudiobook";
import { useSignalR, HubEventToken } from "@/signalr/hub";
import { useOperationProgress } from "../../composables/useOperationProgress";
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
// The open panel is tracked by the book's path, not its index: the list is mutated while it is
// open (a finished import removes a row), and an index-keyed panel would silently re-point at
// whichever book shifted into that slot - with Vue reusing the open BookOrganize form for it.
const discoveredActivePanel: Ref<string | null> = ref(null);
const discoveredCurrentPage: Ref<number> = ref(1);
const discoveredTotalItems: Ref<number> = ref(0);

// A Set behind a ref is a reactive proxy in Vue 3 - add/delete already notify dependents, so
// this is mutated in place. Reassigning a clone (as this used to, at five call sites) copied
// every selected path on each checkbox tick and invalidated the dependent computeds by
// identity rather than by key.
const selectedPaths: Ref<Set<string>> = ref(new Set());
const importConfirmDialog: Ref<boolean> = ref(false);
const snackbar: Ref<boolean> = ref(false);
const snackbarText: Ref<string> = ref("");

const scanMessage: Ref<string> = ref("");
const scanComplete: Ref<boolean> = ref(false);
const scanNewFiles: Ref<number> = ref(0);
const scanTrackedFiles: Ref<number> = ref(0);

const {
  isRunning: scanning,
  processed: scanFilesScanned,
  total: scanTotalFiles,
  start: startScanning,
} = useOperationProgress<LibraryScanProgress, LibraryScanComplete>({
  key: "library-scan",
  progressToken: LibraryScanProgressToken,
  completeToken: LibraryScanCompleteToken,
  getProcessed: (arg) => arg.filesScanned,
  getTotal: (arg) => arg.totalFiles,
  onProgress: (arg) => {
    scanMessage.value = arg.message;
  },
  onComplete: (arg) => {
    scanComplete.value = true;
    scanNewFiles.value = arg.newFilesDiscovered;
    scanTrackedFiles.value = arg.alreadyTracked;
    loadDiscoveredBooks();
  },
});

const {
  isRunning: importing,
  processed: importProcessed,
  total: importTotal,
  start: startImporting,
} = useOperationProgress<DiscoveredImportProgress, DiscoveredImportComplete>({
  key: "discovered-import",
  progressToken: DiscoveredImportProgressToken,
  completeToken: DiscoveredImportCompleteToken,
  getProcessed: (arg) => arg.processed,
  getTotal: (arg) => arg.total,
  onComplete: (arg) => {
    let msg = `Import complete: ${arg.totalSucceeded} of ${arg.totalProcessed} books added`;
    if (arg.totalFailed > 0) {
      msg += ` (${arg.totalFailed} failed)`;
    }
    snackbarText.value = msg;
    snackbar.value = true;
    selectedPaths.value = new Set();
    loadDiscoveredBooks();
  },
});

const onUpdateProgress = (arg: ProgressUpdate) => {
  const book = discoveredBooks.value.find(
    (x) => x.queueId === arg.originalFileLocation,
  );
  if (!book) {
    return;
  }
  book.queueMessage = arg.progressMessage;
  book.queueProgress = arg.progress;

  // The organize succeeded (the discovered row has already been untracked server-side by
  // OrganizeWorker) - drop it from the visible list without waiting for a full reload.
  if (arg.progress >= 100) {
    discoveredBooks.value = discoveredBooks.value.filter((b) => b !== book);
    selectedPaths.value.delete(book.fullPath);
    if (discoveredActivePanel.value === book.fullPath) {
      discoveredActivePanel.value = null;
    }
  }
};

const onQueueError = (arg: QueueError) => {
  // Bulk-import failures never set queueId (that's only used by the single-book
  // organize queue flow), so fall back to matching on fullPath to surface those too.
  const book = discoveredBooks.value.find(
    (x) =>
      x.queueId === arg.originalFileLocation ||
      x.fullPath === arg.originalFileLocation,
  );
  if (book) {
    book.error = arg.error;
  }
};

signalR.on(UpdateProgress, onUpdateProgress);
signalR.on(QueueErrorToken, onQueueError);

onUnmounted(() => {
  signalR.off(UpdateProgress, onUpdateProgress);
  signalR.off(QueueErrorToken, onQueueError);
  signalR.offReconnected(loadDiscoveredBooks);
  // A pending debounce would fire after unmount, mutating dead refs and issuing a wasted request.
  debouncedDiscoveredSearch.cancel();
});

const discoveredTotalPages = computed((): number =>
  Math.ceil(discoveredTotalItems.value / limit),
);

const wellTaggedBooks = computed((): DiscoveredAudiobook[] =>
  discoveredBooks.value.filter((b) => b.isWellTagged && !b.isDuplicate),
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
};

const toggleBookSelected = (book: DiscoveredAudiobook) => {
  if (book.isDuplicate) {
    return;
  }
  if (selectedPaths.value.has(book.fullPath)) {
    selectedPaths.value.delete(book.fullPath);
  } else {
    selectedPaths.value.add(book.fullPath);
  }
};

const onImportSelectedClick = () => {
  importConfirmDialog.value = true;
};

const confirmImportSelected = async () => {
  importConfirmDialog.value = false;
  startImporting();
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
  startScanning();
  scanComplete.value = false;
  scanMessage.value = "";
  await LibraryService.startLibraryScan();
};

// Only the newest request may write to the list: filter keystrokes and page changes overlap,
// and an older response landing last would render a page the user has already moved off.
let loadRequestId = 0;

const loadDiscoveredBooks = async () => {
  const requestId = ++loadRequestId;
  try {
    const result = await LibraryService.getDiscoveredBooks(
      limit,
      (discoveredCurrentPage.value - 1) * limit,
      discoveredSearchQuery.value || undefined,
    );

    if (requestId !== loadRequestId) return;

    discoveredTotalItems.value = result.total;
    discoveredBooks.value = result.items;
  } catch {
    // This is called from SignalR completion callbacks and the reconnect handler as well as
    // from click handlers, so an unhandled rejection here escapes into a hub callback with
    // nothing to catch it - and the stale list gives the user no sign anything went wrong.
    snackbarText.value = "Failed to refresh the discovered books list";
    snackbar.value = true;
  }
};

// A queued book's UpdateProgress/QueueError events can be missed while disconnected (e.g. a
// backgrounded mobile tab), leaving it stuck showing "Queued" even after it finished. Reloading
// the list on reconnect re-syncs queue state the same way it does on mount.
signalR.onReconnected(loadDiscoveredBooks);

const debouncedDiscoveredSearch = debounce(() => {
  // Resetting the page already triggers the `discoveredCurrentPage` watcher, so calling the
  // loader as well issued two overlapping requests per search from any page but the first.
  // (The loadRequestId guard meant only one ever rendered, so this was wasted work, not a bug.)
  if (discoveredCurrentPage.value !== 1) {
    discoveredCurrentPage.value = 1;
  } else {
    loadDiscoveredBooks();
  }
}, 300);

watch(discoveredSearchQuery, () => {
  debouncedDiscoveredSearch();
});

const markDiscoveredAsQueued = (book: DiscoveredAudiobook, queueId: string) => {
  // The discovered row itself isn't untracked here: OrganizeWorker only deletes it once the
  // organize actually succeeds, so a failure (e.g. a duplicate collision) leaves the row in
  // place to retry or resolve instead of disappearing based on a SignalR event that might be
  // missed by a disconnected client.
  book.queueId = queueId;
  if (discoveredActivePanel.value === book.fullPath) {
    discoveredActivePanel.value = null;
  }
  selectedPaths.value.delete(book.fullPath);
};

const removeDiscoveredBook = (book: DiscoveredAudiobook) => {
  discoveredBooks.value = discoveredBooks.value.filter((b) => b != book);
  selectedPaths.value.delete(book.fullPath);

  if (discoveredActivePanel.value === book.fullPath) {
    discoveredActivePanel.value = null;
  }

  // The file itself is already gone at this point (deleted via BookDeleteDialog); untrack the
  // now-stale discovered row too, rather than leaving it until the next full library scan.
  LibraryService.deleteDiscoveredBook(book.fullPath);
};

const formatFileSize = (size: number) => {
  const sizeInMb = size / 1000000;
  return `${sizeInMb.toFixed(1)} MB`;
};

onMounted(() => {
  loadDiscoveredBooks();
});
</script>
