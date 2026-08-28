<template>
  <v-container>
    <v-row class="text-center">
      <v-col class="mb-5">
        <h2 class="headline font-weight-bold mb-5">Books</h2>
      </v-col>
    </v-row>
    <v-row class="text-center">
      <v-col
        class="mb-5"
        cols="12"
      >
        <v-progress-circular
          v-if="loadingBooks"
          indeterminate
          size="23"
          :width="2"
        />

        <template v-if="books.length">
          <v-expansion-panels v-model="activePanel">
            <v-expansion-panel
              v-for="book in books"
              :key="book.fullPath"
              :value="book.fullPath"
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
                  @book-queued="(id) => markBookAsQueued(book, id)"
                  @book-deleted="() => removeBook(book)"
                />
              </v-expansion-panel-text>
            </v-expansion-panel>
          </v-expansion-panels>
          <v-pagination
            v-model="currentPage"
            :length="totalPages"
          ></v-pagination>
        </template>

        <v-row>
          <v-col
            cols="12"
            v-if="!books.length"
          >
            No books
          </v-col>
          <v-col cols="12">
            <v-btn @click="loadBooks()">Load books</v-btn>
          </v-col>
        </v-row>
      </v-col>
    </v-row>
  </v-container>
</template>

<script setup lang="ts">
import { computed, onUnmounted, Ref, ref, watch } from "vue";
import BookOrganize from "./BookOrganize.vue";
import UntaggedService from "../services/UntaggedService";
import QueueService from "../services/QueueService";
import BookFileInfo from "../types/BookFileInfo";
import { useSignalR, HubEventToken } from "@/signalr/hub";
import { ProgressUpdate } from "../signalr/ProgressUpdate";
import { QueueError } from "../signalr/QueueError";

const UpdateProgress: HubEventToken<ProgressUpdate> = "UpdateProgress";
const QueueErrorToken: HubEventToken<QueueError> = "QueueError";

const signalR = useSignalR();

const onUpdateProgress = (arg: ProgressUpdate) => {
  const book = books.value.find((x) => x.queueId === arg.originalFileLocation);
  if (book) {
    book.queueMessage = arg.progressMessage;
    book.queueProgress = arg.progress;
  }
};

const onQueueError = (arg: QueueError) => {
  const book = books.value.find((x) => x.queueId === arg.originalFileLocation);
  if (book) {
    book.error = arg.error;
  }
};

signalR.on(UpdateProgress, onUpdateProgress);
signalR.on(QueueErrorToken, onQueueError);

onUnmounted(() => {
  signalR.off(UpdateProgress, onUpdateProgress);
  signalR.off(QueueErrorToken, onQueueError);
  signalR.offReconnected(loadBooks);
});

const limit = 50;

const books: Ref<BookFileInfo[]> = ref([]);
// Keyed by path, not index: removeBook mutates the array while a panel is open, and an
// index-keyed panel would re-point at whichever book shifted into that slot.
const activePanel: Ref<string | null> = ref(null);
const currentPage: Ref<number> = ref(1);
const totalItems: Ref<number> = ref(0);
const loadingBooks: Ref<boolean> = ref(false);

const totalPages = computed((): number => Math.ceil(totalItems.value / limit));

watch(currentPage, () => {
  loadBooks();
});

// Only the newest load may write to the list: the button and the page watcher can both be in
// flight, and an older response landing last would show the wrong page.
let loadRequestId = 0;

const loadBooks = async () => {
  const requestId = ++loadRequestId;
  loadingBooks.value = true;
  books.value = [];

  try {
    const result = await UntaggedService.getUntagged(
      limit,
      (currentPage.value - 1) * limit,
    );
    const queuedBooks = await QueueService.getQueuedBooks();

    if (requestId !== loadRequestId) return;

    totalItems.value = result.total;
    books.value = enhanceBooksWithQueueInfo(result.items, queuedBooks);
  } finally {
    if (requestId === loadRequestId) {
      loadingBooks.value = false;
    }
  }
};

// A queued book's UpdateProgress/QueueError events can be missed while disconnected (e.g. a
// backgrounded mobile tab), leaving it stuck showing "Queued" even after it finished. Reloading
// the list on reconnect re-syncs queue state the same way it does on mount.
signalR.onReconnected(loadBooks);

const enhanceBooksWithQueueInfo = (
  books: BookFileInfo[],
  queuedBooks: string[],
) => {
  return books.map((b) => {
    if (queuedBooks.indexOf(b.fullPath) !== -1) {
      b.queueId = b.fullPath;
    }

    return b;
  });
};

const markBookAsQueued = (book: BookFileInfo, queueId: string) => {
  book.queueId = queueId;
  if (activePanel.value === book.fullPath) {
    activePanel.value = null;
  }
};

const removeBook = (book: BookFileInfo) => {
  books.value = books.value.filter((b) => b != book);

  if (activePanel.value === book.fullPath) {
    activePanel.value = null;
  }
};

const formatFileSize = (size: number) => {
  const sizeInMb = size / 1000000;
  return `${sizeInMb.toFixed(1)} MB`;
};
</script>
