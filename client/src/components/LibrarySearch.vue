<template>
  <v-menu
    v-model="menuOpen"
    :close-on-content-click="false"
    location="bottom start"
    min-width="360"
    max-width="480"
  >
    <template v-slot:activator="{ props }">
      <v-text-field
        v-bind="props"
        v-model="query"
        placeholder="Search books, authors, series"
        prepend-inner-icon="mdi-magnify"
        clearable
        hide-details
        density="compact"
        variant="solo-filled"
        flat
        style="max-width: 360px"
        @focus="onFocus"
        @keydown.esc="menuOpen = false"
      />
    </template>

    <v-card>
      <v-list v-if="hasResults">
        <template v-if="results.books.length">
          <v-list-subheader>Books</v-list-subheader>
          <v-list-item
            v-for="book in results.books"
            :key="`book-${book.id}`"
            :to="`/library/book/${book.id}`"
            prepend-icon="mdi-book"
            @click="menuOpen = false"
          >
            <v-list-item-title>{{ book.bookName }}</v-list-item-title>
            <v-list-item-subtitle>
              <span v-if="book.authors.length">{{
                book.authors.join(", ")
              }}</span>
              <span v-if="book.series"> &middot; {{ book.series }} </span>
            </v-list-item-subtitle>
          </v-list-item>
        </template>

        <template v-if="results.authors.length">
          <v-list-subheader>Authors</v-list-subheader>
          <v-list-item
            v-for="author in results.authors"
            :key="`author-${author.id}`"
            :to="`/library/authors/${author.id}`"
            prepend-icon="mdi-account"
            @click="menuOpen = false"
          >
            <v-list-item-title>{{ author.name }}</v-list-item-title>
            <v-list-item-subtitle>
              {{ author.bookCount }}
              {{ author.bookCount === 1 ? "book" : "books" }}
            </v-list-item-subtitle>
          </v-list-item>
        </template>

        <template v-if="results.series.length">
          <v-list-subheader>Series</v-list-subheader>
          <v-list-item
            v-for="series in results.series"
            :key="`series-${series.name}`"
            :to="`/library/series/${encodeURIComponent(series.name)}`"
            prepend-icon="mdi-bookshelf"
            @click="menuOpen = false"
          >
            <v-list-item-title>{{ series.name }}</v-list-item-title>
            <v-list-item-subtitle>
              {{ series.bookCount }}
              {{ series.bookCount === 1 ? "book" : "books" }}
            </v-list-item-subtitle>
          </v-list-item>
        </template>
      </v-list>
      <v-card-text v-else-if="searched">No results found</v-card-text>
    </v-card>
  </v-menu>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { debounce } from "lodash";
import LibrarySearchService from "../services/LibrarySearchService";
import LibrarySearchResult from "../types/LibrarySearchResult";

const MIN_QUERY_LENGTH = 2;

const query = ref("");
const menuOpen = ref(false);
const searched = ref(false);
const results = ref<LibrarySearchResult>({
  books: [],
  authors: [],
  series: [],
});

let requestId = 0;

const hasResults = computed(
  () =>
    results.value.books.length > 0 ||
    results.value.authors.length > 0 ||
    results.value.series.length > 0,
);

const runSearch = async (value: string) => {
  const currentRequestId = ++requestId;
  const result = await LibrarySearchService.searchLibrary(value);
  if (currentRequestId !== requestId) {
    return;
  }
  results.value = result;
  searched.value = true;
  menuOpen.value = true;
};

const debouncedSearch = debounce((value: string) => {
  runSearch(value);
}, 250);

watch(query, (value) => {
  const trimmed = (value ?? "").trim();
  if (trimmed.length < MIN_QUERY_LENGTH) {
    requestId++;
    results.value = { books: [], authors: [], series: [] };
    searched.value = false;
    menuOpen.value = false;
    return;
  }
  debouncedSearch(trimmed);
});

const onFocus = () => {
  if (hasResults.value || searched.value) {
    menuOpen.value = true;
  }
};
</script>
