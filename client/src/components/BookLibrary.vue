<template>
  <v-container>
    <v-row class="text-center">
      <v-col class="mb-5">
        <h2 class="headline font-weight-bold mb-5">Library</h2>
      </v-col>
    </v-row>
    <v-row>
      <v-col cols="12">
        <h3 class="text-h6 mb-1">Managed Audiobooks</h3>
        <p class="text-body-2 text-medium-emphasis mb-3">
          All audiobooks tracked in the library. Books flagged with a warning
          chip have consistency issues.
        </p>
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
            class="d-flex align-center ga-2"
          >
            <v-btn
              prepend-icon="mdi-account-group"
              to="/library/authors"
            >
              Browse by Author
            </v-btn>
            <v-btn
              prepend-icon="mdi-file-find"
              to="/library/discovered"
            >
              Discovered Audiobooks
            </v-btn>
          </v-col>
        </v-row>
        <template v-if="books.length">
          <v-list>
            <v-list-item
              v-for="book in books"
              :key="book.id"
              :to="`/library/book/${book.id}`"
              class="cursor-pointer"
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
              <template v-slot:append>
                <v-chip
                  v-if="issueSummary[book.id]"
                  size="x-small"
                  color="warning"
                >
                  {{ issueSummary[book.id] }}
                  {{ issueSummary[book.id] === 1 ? "issue" : "issues" }}
                </v-chip>
              </template>
            </v-list-item>
          </v-list>
          <v-pagination
            v-model="currentPage"
            :length="totalPages"
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
import { computed, onMounted, Ref, ref, watch } from "vue";
import { debounce } from "lodash";
import BrowseService from "../services/BrowseService";
import ConsistencyService from "../services/ConsistencyService";
import { formatDuration } from "../helpers/formatHelpers";
import ManagedAudiobook from "../types/ManagedAudiobook";

const limit = 50;

const searchQuery: Ref<string> = ref("");

const books: Ref<ManagedAudiobook[]> = ref([]);
const currentPage: Ref<number> = ref(1);
const totalItems: Ref<number> = ref(0);

const issueSummary: Ref<Record<number, number>> = ref({});

const totalPages = computed((): number => Math.ceil(totalItems.value / limit));

watch(currentPage, () => {
  loadBooks();
  // Re-check for issues each time the visible page changes, so chips reflect issues
  // resolved elsewhere (LibraryConsistency, BookDetail) since the summary was last loaded.
  loadIssueSummary();
});

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
  // Also refresh on search, for the same reason as the pagination watcher above.
  loadIssueSummary();
}, 300);

watch(searchQuery, () => {
  debouncedSearch();
});

const loadIssueSummary = async () => {
  try {
    issueSummary.value = await ConsistencyService.getIssueSummary();
  } catch {
    issueSummary.value = {};
  }
};

onMounted(() => {
  loadBooks();
  loadIssueSummary();
});
</script>
