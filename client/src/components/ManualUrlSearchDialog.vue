<template>
  <v-card :width="dialogWidth">
    <v-toolbar
      dark
      prominent
    >
      <v-btn
        icon
        dark
        @click="$emit('resultChosen', undefined)"
      >
        <v-icon>mdi-close</v-icon>
      </v-btn>
    </v-toolbar>
    <v-card-text>
      <v-form
        validate-on="input"
        @submit.prevent="submit"
        v-model="validForm"
      >
        <v-container>
          <v-row>
            <v-col>
              <v-text-field
                label="Book URL"
                :hint="urlHint"
                persistent-hint
                single-line
                :rules="rules"
                clearable
                v-model="bookUrl"
              ></v-text-field>
            </v-col>

            <v-col cols="2">
              <v-btn
                color="primary"
                type="button"
                @click="submit"
              >
                <v-icon>mdi-magnify</v-icon>
                Submit
              </v-btn>
            </v-col>
          </v-row>
        </v-container>
      </v-form>

      <template v-if="selectedResult">
        <SeriesSelectionTable
          :series="selectedResult.series"
          @series-chosen="chooseSeries"
        />
      </template>
    </v-card-text>

    <ErrorNotifications
      :errors="errors"
      @error-dismissed="onErrorDismissed"
    />
  </v-card>
</template>

<script setup lang="ts">
import { computed, onMounted, Ref, ref } from "vue";
import ErrorNotifications from "./ErrorNotifications.vue";
import SeriesSelectionTable from "./SeriesSelectionTable.vue";
import { useErrors } from "./errors";
import { BookSearchResult } from "@/types/BookSearchResult";
import { SearchServiceInfo } from "@/types/SearchServiceInfo";
import SearchService from "@/services/SearchService";

const validForm = ref(false);
const bookUrl = ref("");
const services: Ref<SearchServiceInfo[]> = ref([]);
const props = defineProps<{ dialogWidth?: string }>();
const selectedResult: Ref<BookSearchResult | undefined> = ref(undefined);
const emit = defineEmits<{
  (e: "resultChosen", result: BookSearchResult | undefined): void;
}>();

const urlHint = computed((): string => {
  if (!services.value.length) {
    return "";
  }
  return `Supports: ${services.value.map((s) => s.name).join(", ")}`;
});

const rules = [(v: any) => !!v || "Url is required"];

const submit = async () => {
  if (!validForm.value) {
    return;
  }
  selectedResult.value = await SearchService.getBookDetails(bookUrl.value);

  if (
    !selectedResult.value.series?.length ||
    selectedResult.value.series.length == 1
  ) {
    emit("resultChosen", selectedResult.value);
  }
};

const chooseSeries = (seriesIdx: number) => {
  if (!selectedResult.value) {
    emit("resultChosen", undefined);
    return;
  }

  selectedResult.value.series = [selectedResult.value.series[seriesIdx]];

  emit("resultChosen", selectedResult.value);
};

onMounted(async () => {
  try {
    services.value = await SearchService.getServices();
  } catch {
    // Hint just stays empty if this fails; not critical to the manual-add flow.
  }
});

const { errors, onErrorDismissed } = useErrors();
</script>

<style scoped>
a {
  color: #bb86fc;
}
</style>
