<template>
  <v-container>
    <v-row>
      <v-col>
        <v-btn
          variant="text"
          prepend-icon="mdi-arrow-left"
          to="/library/authors"
        >
          Back to Authors
        </v-btn>
      </v-col>
    </v-row>
    <template v-if="detail">
      <v-row>
        <v-col>
          <h2 class="text-h5">{{ detail.author.name }}</h2>
          <div class="text-subtitle-1">
            {{ detail.author.bookCount }}
            {{ detail.author.bookCount === 1 ? "book" : "books" }}
          </div>
        </v-col>
      </v-row>
      <v-row v-if="detail.series.length">
        <v-col>
          <h3 class="text-h6 mb-2">Series</h3>
          <v-list>
            <v-list-item
              v-for="s in detail.series"
              :key="s.seriesName"
              :to="`/library/series/${encodeURIComponent(s.seriesName)}?authorId=${detail.author.id}`"
            >
              <v-list-item-title>{{ s.seriesName }}</v-list-item-title>
              <v-list-item-subtitle>
                {{ s.bookCount }}
                {{ s.bookCount === 1 ? "book" : "books" }}
              </v-list-item-subtitle>
              <template v-slot:append>
                <v-icon>mdi-chevron-right</v-icon>
              </template>
            </v-list-item>
          </v-list>
        </v-col>
      </v-row>
      <v-row v-if="detail.standaloneBooks.length">
        <v-col>
          <h3 class="text-h6 mb-2">Standalone Books</h3>
          <v-list>
            <v-list-item
              v-for="book in detail.standaloneBooks"
              :key="book.id"
            >
              <v-list-item-title>{{ book.bookName }}</v-list-item-title>
              <v-list-item-subtitle>
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
        </v-col>
      </v-row>
    </template>
    <div
      v-else-if="loading"
      class="text-center mt-5"
    >
      <v-progress-circular indeterminate />
    </div>
    <div
      v-else
      class="text-center mt-5"
    >
      Author not found
    </div>
  </v-container>
</template>

<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRoute } from "vue-router";
import BrowseService from "../../services/BrowseService";
import { formatDuration } from "../../helpers/formatHelpers";
import AuthorDetailType from "../../types/AuthorDetail";

const route = useRoute();
const detail = ref<AuthorDetailType | null>(null);
const loading = ref(false);

onMounted(async () => {
  loading.value = true;
  try {
    const authorId = Number(route.params.authorId);
    detail.value = await BrowseService.getAuthorDetail(authorId);
  } finally {
    loading.value = false;
  }
});
</script>
