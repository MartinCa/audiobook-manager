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
    <v-row class="text-center">
      <v-col
        class="mb-5"
        cols="12"
      >
        <template v-if="books.length">
          <v-expansion-panels v-model="activePanel">
            <v-expansion-panel
              v-for="(book, i) in books"
              :key="i"
            >
              <v-expansion-panel-title>
                <v-row>
                  <v-col> {{ book.author }} - {{ book.book_name }} </v-col>
                </v-row>
              </v-expansion-panel-title>
              <v-expansion-panel-text>
                <BookOrganize
                  :book-path="book.path"
                  @book-organized="removeBook(book)"
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
import BookOrganize from "./BookOrganize.vue";
import LibraryService from "../services/LibraryService";
import ManagedAudiobook from "../types/ManagedAudiobook";
import { useSignalR, HubEventToken } from "@quangdao/vue-signalr";
import { LibraryScanProgress } from "../signalr/LibraryScanProgress";
import { LibraryScanComplete } from "../signalr/LibraryScanComplete";

const LibraryScanProgressToken: HubEventToken<LibraryScanProgress> =
  "LibraryScanProgress";
const LibraryScanCompleteToken: HubEventToken<LibraryScanComplete> =
  "LibraryScanComplete";

const signalR = useSignalR();

const limit = 50;

const books: Ref<ManagedAudiobook[]> = ref([]);
const activePanel: Ref<any> = ref(null);
const currentPage: Ref<number> = ref(1);
const totalItems: Ref<number> = ref(0);

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
});

const totalPages = computed((): number => Math.ceil(totalItems.value / limit));

watch(currentPage, (oldPage, newPage) => {
  loadBooks();
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
  const result = await LibraryService.getBooks(
    limit,
    (currentPage.value - 1) * limit,
  );
  totalItems.value = result.total;
  books.value = result.items;
};

const removeBook = (book: ManagedAudiobook) => {
  books.value = books.value.filter((b) => b != book);
  activePanel.value = null;
};
</script>
