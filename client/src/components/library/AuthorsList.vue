<template>
  <v-container>
    <v-row>
      <v-col>
        <v-btn
          variant="text"
          prepend-icon="mdi-arrow-left"
          to="/library"
        >
          Back to Library
        </v-btn>
      </v-col>
    </v-row>
    <v-row>
      <v-col>
        <h2 class="text-h5 mb-3">Authors</h2>
      </v-col>
    </v-row>
    <v-row>
      <v-col
        cols="12"
        md="6"
      >
        <v-text-field
          v-model="filter"
          label="Filter authors"
          prepend-inner-icon="mdi-magnify"
          clearable
          hide-details
          density="compact"
        />
      </v-col>
    </v-row>
    <v-row>
      <v-col>
        <v-list v-if="filteredAuthors.length">
          <v-list-item
            v-for="author in filteredAuthors"
            :key="author.id"
            :to="`/library/authors/${author.id}`"
          >
            <v-list-item-title>{{ author.name }}</v-list-item-title>
            <v-list-item-subtitle>
              {{ author.bookCount }}
              {{ author.bookCount === 1 ? "book" : "books" }}
            </v-list-item-subtitle>
            <template v-slot:append>
              <v-icon>mdi-chevron-right</v-icon>
            </template>
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
          No authors found
        </div>
      </v-col>
    </v-row>
  </v-container>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import BrowseService from "../../services/BrowseService";
import AuthorSummary from "../../types/AuthorSummary";

const authors = ref<AuthorSummary[]>([]);
const filter = ref("");
const loading = ref(false);

const filteredAuthors = computed(() => {
  if (!filter.value) return authors.value;
  const q = filter.value.toLowerCase();
  return authors.value.filter((a) => a.name.toLowerCase().includes(q));
});

onMounted(async () => {
  loading.value = true;
  try {
    authors.value = await BrowseService.getAuthors();
  } finally {
    loading.value = false;
  }
});
</script>
