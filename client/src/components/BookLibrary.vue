<template>
  <v-container>
    <v-row class="text-center">
      <v-col class="mb-5">
        <h2 class="headline font-weight-bold mb-5">Library</h2>
      </v-col>
    </v-row>
    <v-row>
      <v-col cols="12">
        <v-btn
          :disabled="scanning"
          :loading="scanning"
          @click="startScan()"
        >
          Scan Library
        </v-btn>
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
        <h3 class="text-h6 mb-3">Discovered Audiobooks</h3>
        <template v-if="discoveredBooks.length">
          <v-expansion-panels v-model="discoveredActivePanel">
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
          <v-pagination
            v-model="discoveredCurrentPage"
            :length="discoveredTotalPages"
            @update:model-value=""
          ></v-pagination>
        </template>
        <template v-else>
          <div class="text-center">No discovered audiobooks</div>
          <v-btn
            class="mt-2"
            @click="loadDiscoveredBooks()"
          >
            Load discovered
          </v-btn>
        </template>
      </v-col>
    </v-row>
    <v-row>
      <v-col cols="12">
        <h3 class="text-h6 mb-3">Managed Audiobooks</h3>
        <v-row class="mb-3">
          <v-col
            cols="12"
            md="6"
          >
            <v-text-field
              v-model="searchQuery"
              label="Search library"
              prepend-inner-icon="mdi-magnify"
              clearable
              hide-details
              density="compact"
            />
          </v-col>
          <v-col
            cols="12"
            md="6"
            class="d-flex align-center"
          >
            <v-btn
              prepend-icon="mdi-account-group"
              to="/library/authors"
            >
              Browse by Author
            </v-btn>
          </v-col>
        </v-row>
        <template v-if="books.length">
          <v-list>
            <v-list-item
              v-for="book in books"
              :key="book.id"
            >
              <v-list-item-title>
                {{ book.authors.join(", ") }} &mdash; {{ book.bookName }}
              </v-list-item-title>
              <v-list-item-subtitle>
                <span v-if="book.series">
                  {{ book.series }}
                  <span v-if="book.seriesPart">#{{ book.seriesPart }}</span>
                  &middot;
                </span>
                <span v-if="book.year">{{ book.year }}</span>
                <span v-if="book.narrators.length">
                  &middot; Narrated by {{ book.narrators.join(", ") }}
                </span>
                <span v-if="book.durationInSeconds">
                  &middot; {{ formatDuration(book.durationInSeconds) }}
                </span>
              </v-list-item-subtitle>
            </v-list-item>
          </v-list>
          <v-pagination
            v-model="currentPage"
            :length="totalPages"
            @update:model-value=""
          ></v-pagination>
        </template>
        <template v-else>
          <v-row>
            <v-col cols="12"> No books in library </v-col>
            <v-col cols="12">
              <v-btn @click="loadBooks()">Load library</v-btn>
            </v-col>
          </v-row>
        </template>
      </v-col>
    </v-row>
  </v-container>
</template>

<script setup lang="ts">
import { computed, Ref, ref, watch } from "vue";
import { debounce } from "lodash";
import BookOrganize from "./BookOrganize.vue";
import LibraryService from "../services/LibraryService";
import BrowseService from "../services/BrowseService";
import { formatDuration } from "../helpers/formatHelpers";
import ManagedAudiobook from "../types/ManagedAudiobook";
import BookFileInfo from "../types/BookFileInfo";
import { useSignalR, HubEventToken } from "@quangdao/vue-signalr";
import { LibraryScanProgress } from "../signalr/LibraryScanProgress";
import { LibraryScanComplete } from "../signalr/LibraryScanComplete";
import { ProgressUpdate } from "../signalr/ProgressUpdate";
import { QueueError } from "../signalr/QueueError";

const LibraryScanProgressToken: HubEventToken<LibraryScanProgress> =
  "LibraryScanProgress";
const LibraryScanCompleteToken: HubEventToken<LibraryScanComplete> =
  "LibraryScanComplete";
const UpdateProgress: HubEventToken<ProgressUpdate> = "UpdateProgress";
const QueueErrorToken: HubEventToken<QueueError> = "QueueError";

const signalR = useSignalR();

const limit = 50;

const searchQuery: Ref<string> = ref("");

const books: Ref<ManagedAudiobook[]> = ref([]);
const currentPage: Ref<number> = ref(1);
const totalItems: Ref<number> = ref(0);

const discoveredBooks: Ref<BookFileInfo[]> = ref([]);
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

signalR.on(LibraryScanProgressToken, (arg) => {
  scanMessage.value = arg.message;
  scanFilesScanned.value = arg.filesScanned;
  scanTotalFiles.value = arg.totalFiles;
});

signalR.on(LibraryScanCompleteToken, (arg) => {
  scanning.value = false;
  scanComplete.value = true;
  scanNewFiles.value = arg.newFilesDiscovered;
  scanTrackedFiles.value = arg.alreadyTracked;
  loadDiscoveredBooks();
});

signalR.on(UpdateProgress, (arg) => {
  const book = discoveredBooks.value.find(
    (x) => x.queueId === arg.originalFileLocation,
  );
  if (book) {
    book.queueMessage = arg.progressMessage;
    book.queueProgress = arg.progress;
  }
});

signalR.on(QueueErrorToken, (arg) => {
  const book = discoveredBooks.value.find(
    (x) => x.queueId === arg.originalFileLocation,
  );
  if (book) {
    book.error = arg.error;
  }
});

const totalPages = computed((): number => Math.ceil(totalItems.value / limit));
const discoveredTotalPages = computed((): number =>
  Math.ceil(discoveredTotalItems.value / limit),
);

watch(currentPage, () => {
  loadBooks();
});

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

const loadBooks = async () => {
  const offset = (currentPage.value - 1) * limit;
  const result = searchQuery.value
    ? await BrowseService.searchBooks(searchQuery.value, limit, offset)
    : await BrowseService.getBooks(limit, offset);
  totalItems.value = result.total;
  books.value = result.items;
};

const debouncedSearch = debounce(() => {
  currentPage.value = 1;
  loadBooks();
}, 300);

watch(searchQuery, () => {
  debouncedSearch();
});

const loadDiscoveredBooks = async () => {
  const result = await LibraryService.getDiscoveredBooks(
    limit,
    (discoveredCurrentPage.value - 1) * limit,
  );
  discoveredTotalItems.value = result.total;
  discoveredBooks.value = result.items;
};

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
</script>
