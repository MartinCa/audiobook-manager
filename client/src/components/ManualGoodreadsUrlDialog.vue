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
                label="Goodreads url"
                single-line
                hide-details
                :rules="rules"
                clearable
                v-model="goodreadsUrl"
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
        <v-row>
          <v-col
            cols="12"
            class="text-center"
            >Select series</v-col
          >
          <v-col
            cols="0"
            lg="3"
          ></v-col>
          <v-col
            cols="12"
            lg="6"
          >
            <v-table>
              <thead>
                <tr>
                  <th>Series</th>
                  <th>Part</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(s, idx) in selectedResult.series">
                  <td>
                    {{ s.seriesName }}
                  </td>
                  <td>
                    {{ s.seriesPart }}
                  </td>
                  <td>
                    <v-btn
                      color="primary"
                      v-bind="props"
                      @click="chooseSeries(idx)"
                    >
                      <v-icon>mdi-check</v-icon>
                    </v-btn>
                  </td>
                </tr>
              </tbody>
            </v-table>
          </v-col>
          <v-col
            cols="0"
            lg="3"
          ></v-col>
        </v-row>
      </template>
    </v-card-text>

    <ErrorNotifications
      :errors="errors"
      @error-dismissed="onErrorDismissed"
    />
  </v-card>
</template>

<script setup lang="ts">
import { Ref, ref } from "vue";
import ErrorNotifications from "./ErrorNotifications.vue";
import { useErrors } from "./errors";
import { BookSearchResult } from "@/types/BookSearchResult";
import SearchService from "@/services/SearchService";

const validForm = ref(false);
const goodreadsUrl = ref("");
const props = defineProps<{ dialogWidth?: string }>();
const selectedResult: Ref<BookSearchResult | undefined> = ref(undefined);
const emit = defineEmits<{
  (e: "resultChosen", result: BookSearchResult | undefined): void;
}>();

const rules = [
  (v: any) =>
    (!!v && /^(https:\/\/)?(www\.)?goodreads\.com\/.+/.test(v)) ||
    "Url is required",
];

const submit = async () => {
  if (!validForm.value) {
    return;
  }
  selectedResult.value = await SearchService.getBookDetails(goodreadsUrl.value);

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

const { errors, onErrorDismissed } = useErrors();
</script>

<style scope>
a {
  color: #bb86fc;
}

span.existing-name {
  color: #bb86fc;
}
</style>
