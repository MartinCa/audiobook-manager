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
              v-for="(book, i) in books"
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
                  @book-queued="(id) => markBookAsQueued(book, id)"
                  @book-deleted="() => removeBook(book)"
                />
              </v-expansion-panel-text>
            </v-expansion-panel>
          </v-expansion-panels>
          <v-pagination
            v-model="currentPage"
            :length="totalPages"
            @update:model-value=""
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
import { computed, Ref, ref, watch } from "vue";
import BookOrganize from "./BookOrganize.vue";
import UntaggedService from "../services/UntaggedService";
import QueueService from "../services/QueueService";
import BookFileInfo from "../types/BookFileInfo";
import { useSignalR, HubEventToken } from "@quangdao/vue-signalr";
import { ProgressUpdate } from "../signalr/ProgressUpdate";
import { QueueError } from "../signalr/QueueError";

const UpdateProgress: HubEventToken<ProgressUpdate> = "UpdateProgress";
const QueueErrorToken: HubEventToken<QueueError> = "QueueError";

const signalR = useSignalR();

signalR.on(UpdateProgress, (arg) => {
  const book = books.value.find((x) => x.queueId === arg.originalFileLocation);
  if (book) {
    book.queueMessage = arg.progressMessage;
    book.queueProgress = arg.progress;
  }
});

signalR.on(QueueErrorToken, (arg) => {
  const book = books.value.find((x) => x.queueId === arg.originalFileLocation);
  if (book) {
    book.error = arg.error;
  }
});

const limit = 50;

const books: Ref<BookFileInfo[]> = ref([]);
const activePanel: Ref<any> = ref(null);
const currentPage: Ref<number> = ref(1);
const totalItems: Ref<number> = ref(0);
const loadingBooks: Ref<boolean> = ref(false);

const totalPages = computed((): number => Math.ceil(totalItems.value / limit));

watch(currentPage, (oldPage, newPage) => {
  loadBooks();
});

const loadBooks = async () => {
  loadingBooks.value = true;
  books.value = [];

  const result = await UntaggedService.getUntagged(
    limit,
    (currentPage.value - 1) * limit,
  );
  const queuedBooks = await QueueService.getQueuedBooks();
  totalItems.value = result.total;
  books.value = enhanceBooksWithQueueInfo(result.items, queuedBooks);

  loadingBooks.value = false;
};

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
  var bookIdx = books.value.indexOf(book);
  book.queueId = queueId;
  if (bookIdx === activePanel.value) {
    activePanel.value = null;
  }
};

const removeBook = (book: BookFileInfo) => {
  var bookIdx = books.value.indexOf(book);
  var currentlyOpen = bookIdx === activePanel.value;

  books.value = books.value.filter((b) => b != book);

  if (currentlyOpen) {
    activePanel.value = null;
  }
};

const formatFileSize = (size: number) => {
  const sizeInMb = size / 1000000;
  return `${sizeInMb.toFixed(1)} MB`;
};
</script>
