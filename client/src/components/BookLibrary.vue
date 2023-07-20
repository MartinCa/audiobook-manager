<template>
  <v-container>
    <v-row class="text-center">
      <v-col class="mb-5">
        <h2 class="headline font-weight-bold mb-5">Library</h2>
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

const limit = 50;

const books: Ref<ManagedAudiobook[]> = ref([]);
const activePanel: Ref<any> = ref(null);
const currentPage: Ref<number> = ref(1);
const totalItems: Ref<number> = ref(0);

const totalPages = computed((): number => Math.ceil(totalItems.value / limit));

watch(currentPage, (oldPage, newPage) => {
  loadBooks();
});

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
