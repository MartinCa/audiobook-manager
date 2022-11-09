<template>
  <v-container>
    <v-row class="text-center">
      <v-col class="mb-5">
        <h2 class="headline font-weight-bold mb-5">
          Books
        </h2>
      </v-col>

    </v-row>
    <v-row class="text-center">

      <v-col class="mb-5"
             cols="12">


        <template v-if="books.length">
          <v-expansion-panels v-model="activePanel">

            <v-expansion-panel v-for="(book, i) in books"
                               :key="i">
              <v-expansion-panel-title>
                <v-row>
                  <v-col>
                    {{ book.fileName }}
                  </v-col>
                  <v-col>
                    {{ formatFileSize(book.sizeInBytes) }}
                  </v-col>
                </v-row>
              </v-expansion-panel-title>
              <v-expansion-panel-text>
                <BookOrganize :book-path="book.fullPath"
                              @book-organized="removeBook(book)" />
              </v-expansion-panel-text>
            </v-expansion-panel>
          </v-expansion-panels>
          <v-pagination v-model="currentPage"
                        :length="totalPages"
                        @update:model-value=""></v-pagination>
        </template>
        <template v-else>
          <v-row>
            <v-col cols="
                        12">
              No books
            </v-col>
            <v-col cols="12">
              <v-btn @click="loadBooks()">Load books</v-btn>
            </v-col>
          </v-row>
        </template>

      </v-col>
    </v-row>
  </v-container>
</template>

<script setup lang="ts">

import { computed, Ref, ref, watch } from 'vue'
import BookOrganize from './BookOrganize.vue';
import UntaggedService from '../services/UntaggedService';
import BookFileInfo from '../types/BookFileInfo';

const limit = 50;

const books: Ref<BookFileInfo[]> = ref([]);
const activePanel: Ref<any> = ref(null);
const currentPage: Ref<number> = ref(1);
const totalItems: Ref<number> = ref(0);

const totalPages = computed((): number => Math.ceil(totalItems.value / limit))

watch(currentPage, (oldPage, newPage) => {
  loadBooks();
});

const loadBooks = async () => {
  const result = await UntaggedService.getUntagged(limit, (currentPage.value - 1) * limit);
  totalItems.value = result.total;
  books.value = result.items;
};


const removeBook = (book: BookFileInfo) => {
  books.value = books.value.filter(b => b != book);
  activePanel.value = null
}

const formatFileSize = (size: number) => {
  const sizeInMb = size / 1000000;
  return `${sizeInMb.toFixed(1)} MB`;
};

</script>
