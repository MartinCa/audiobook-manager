<template>
  <v-container>
    <v-row>
      <v-col>
        <v-btn
          variant="text"
          prepend-icon="mdi-arrow-left"
          @click="goBack"
        >
          Back
        </v-btn>
      </v-col>
    </v-row>
    <v-row>
      <v-col>
        <h2 class="text-h5">{{ seriesName }}</h2>
        <div class="text-subtitle-1">
          {{ books.length }} {{ books.length === 1 ? "book" : "books" }}
        </div>
      </v-col>
    </v-row>
    <v-row>
      <v-col>
        <v-list v-if="books.length">
          <v-list-item
            v-for="book in books"
            :key="book.id"
          >
            <v-list-item-title>
              <span v-if="book.seriesPart"
                >Part {{ book.seriesPart }} &mdash;
              </span>
              {{ book.bookName }}
            </v-list-item-title>
            <v-list-item-subtitle>
              <span v-if="book.authors.length">{{
                book.authors.join(", ")
              }}</span>
              <span v-if="book.year"> &middot; {{ book.year }}</span>
              <span v-if="book.narrators.length">
                &middot; Narrated by {{ book.narrators.join(", ") }}
              </span>
              <span v-if="book.durationInSeconds">
                &middot; {{ formatDuration(book.durationInSeconds) }}
              </span>
            </v-list-item-subtitle>
          </v-list-item>
        </v-list>
        <div
          v-else-if="loading"
          class="text-center"
        >
          <v-progress-circular indeterminate />
        </div>
        <div
          v-else
          class="text-center"
        >
          No books found in this series
        </div>
      </v-col>
    </v-row>
  </v-container>
</template>

<script setup lang="ts">
import { onMounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import BrowseService from "../../services/BrowseService";
import { formatDuration } from "../../helpers/formatHelpers";
import ManagedAudiobook from "../../types/ManagedAudiobook";

const route = useRoute();
const router = useRouter();
const books = ref<ManagedAudiobook[]>([]);
const loading = ref(false);
const seriesName = ref("");

const goBack = () => {
  const authorId = route.query.authorId;
  if (authorId) {
    router.push(`/library/authors/${authorId}`);
  } else {
    router.push("/library");
  }
};

onMounted(async () => {
  loading.value = true;
  try {
    seriesName.value = route.params.seriesName as string;
    const authorId = route.query.authorId
      ? Number(route.query.authorId)
      : undefined;
    books.value = await BrowseService.getSeriesBooks(
      seriesName.value,
      authorId,
    );
  } finally {
    loading.value = false;
  }
});
</script>
