<template>
  <v-card :width="dialogWidth">
    <v-card-title>Preview Tag Changes</v-card-title>
    <v-card-text>
      <v-table density="compact">
        <thead>
          <tr>
            <th style="width: 30px">
              <v-checkbox-btn
                :model-value="allSelected"
                :indeterminate="someSelected && !allSelected"
                @update:model-value="toggleAll"
              />
            </th>
            <th>Field</th>
            <th>Current</th>
            <th>New</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="field in fields"
            :key="field.key"
            :class="{ 'text-grey': !field.changed }"
          >
            <td>
              <v-checkbox-btn
                :model-value="selected.has(field.key)"
                :disabled="!field.changed"
                @update:model-value="(v: boolean) => toggleField(field.key, v)"
              />
            </td>
            <td>{{ field.label }}</td>
            <td
              class="text-wrap"
              style="max-width: 300px"
            >
              <template v-if="field.key === 'cover'">
                <span v-if="currentInput.cover_base64">Has cover</span>
                <span
                  v-else
                  class="text-grey"
                  >No cover</span
                >
              </template>
              <template v-else>{{ field.currentValue || "—" }}</template>
            </td>
            <td
              class="text-wrap"
              style="max-width: 300px"
            >
              <template v-if="field.key === 'cover'">
                <span v-if="searchResult.imageUrl">{{
                  searchResult.imageUrl
                }}</span>
                <span
                  v-else
                  class="text-grey"
                  >No cover</span
                >
              </template>
              <template v-else>
                <span :class="{ 'font-weight-bold': field.changed }">
                  {{ field.newValue || "—" }}
                </span>
              </template>
            </td>
          </tr>
        </tbody>
      </v-table>
    </v-card-text>
    <v-card-actions>
      <v-spacer />
      <v-btn @click="$emit('cancel')">Cancel</v-btn>
      <v-btn
        color="primary"
        variant="outlined"
        @click="applySelected"
        :disabled="selected.size === 0"
      >
        Apply Selected ({{ selected.size }})
      </v-btn>
      <v-btn
        color="primary"
        @click="applyAll"
      >
        Apply All
      </v-btn>
    </v-card-actions>
  </v-card>
</template>

<script setup lang="ts">
import { computed, ref } from "vue";
import OrganizeAudiobookInput from "../types/OrganizeAudiobookInput";
import { BookSearchResult } from "../types/BookSearchResult";
import { joinPersons } from "../helpers/bookDetailsHelpers";

const props = defineProps<{
  dialogWidth: string;
  currentInput: OrganizeAudiobookInput;
  searchResult: BookSearchResult;
}>();

const emit = defineEmits<{
  (e: "apply", result: BookSearchResult, selectedFields: Set<string>): void;
  (e: "cancel"): void;
}>();

interface FieldDiff {
  key: string;
  label: string;
  currentValue: string;
  newValue: string;
  changed: boolean;
}

const fields = computed((): FieldDiff[] => {
  const cur = props.currentInput;
  const res = props.searchResult;

  const newAuthors = joinPersons(res.authors) ?? "";
  const newNarrators = joinPersons(res.narrators) ?? "";
  const newSeries = res.series?.length ? res.series[0].seriesName : "";
  const newSeriesPart = res.series?.length
    ? (res.series[0].seriesPart ?? "")
    : "";
  const newGenres = res.genres?.join("/") ?? "";

  return [
    {
      key: "authors",
      label: "Authors",
      currentValue: cur.authors ?? "",
      newValue: newAuthors,
      changed: (cur.authors ?? "") !== newAuthors,
    },
    {
      key: "narrators",
      label: "Narrators",
      currentValue: cur.narrators ?? "",
      newValue: newNarrators,
      changed: (cur.narrators ?? "") !== newNarrators,
    },
    {
      key: "bookName",
      label: "Book Name",
      currentValue: cur.bookName ?? "",
      newValue: res.bookName ?? "",
      changed: (cur.bookName ?? "") !== (res.bookName ?? ""),
    },
    {
      key: "subtitle",
      label: "Subtitle",
      currentValue: cur.subtitle ?? "",
      newValue: res.subtitle ?? "",
      changed: (cur.subtitle ?? "") !== (res.subtitle ?? ""),
    },
    {
      key: "series",
      label: "Series",
      currentValue:
        [cur.series, cur.seriesPart].filter(Boolean).join(" #") || "",
      newValue: [newSeries, newSeriesPart].filter(Boolean).join(" #") || "",
      changed:
        (cur.series ?? "") !== newSeries ||
        (cur.seriesPart ?? "") !== newSeriesPart,
    },
    {
      key: "year",
      label: "Year",
      currentValue: cur.year?.toString() ?? "",
      newValue: res.year?.toString() ?? "",
      changed: cur.year !== res.year,
    },
    {
      key: "genres",
      label: "Genres",
      currentValue: cur.genres ?? "",
      newValue: newGenres,
      changed: (cur.genres ?? "") !== newGenres,
    },
    {
      key: "description",
      label: "Description",
      currentValue: truncate(cur.description ?? "", 100),
      newValue: truncate(res.description ?? "", 100),
      changed: (cur.description ?? "") !== (res.description ?? ""),
    },
    {
      key: "rating",
      label: "Rating",
      currentValue: cur.rating?.toString() ?? "",
      newValue: res.rating?.toString() ?? "",
      changed: cur.rating?.toString() !== res.rating?.toString(),
    },
    {
      key: "publisher",
      label: "Publisher",
      currentValue: cur.publisher ?? "",
      newValue: res.publisher ?? "",
      changed: (cur.publisher ?? "") !== (res.publisher ?? ""),
    },
    {
      key: "copyright",
      label: "Copyright",
      currentValue: cur.copyright ?? "",
      newValue: res.copyright ?? "",
      changed: (cur.copyright ?? "") !== (res.copyright ?? ""),
    },
    {
      key: "asin",
      label: "ASIN",
      currentValue: cur.asin ?? "",
      newValue: res.asin ?? "",
      changed: (cur.asin ?? "") !== (res.asin ?? ""),
    },
    {
      key: "www",
      label: "URL",
      currentValue: cur.www ?? "",
      newValue: res.url ?? "",
      changed: (cur.www ?? "") !== (res.url ?? ""),
    },
    {
      key: "cover",
      label: "Cover",
      currentValue: cur.cover_base64 ? "Has cover" : "",
      newValue: res.imageUrl ?? "",
      changed: !!res.imageUrl,
    },
  ];
});

const selected = ref<Set<string>>(new Set());

// Auto-select changed fields on mount
const changedFields = computed(() =>
  fields.value.filter((f) => f.changed).map((f) => f.key),
);

// Initialize selection with changed fields
selected.value = new Set(changedFields.value);

const allSelected = computed(
  () =>
    changedFields.value.length > 0 &&
    changedFields.value.every((k) => selected.value.has(k)),
);
const someSelected = computed(() => selected.value.size > 0);

const toggleField = (key: string, value: boolean) => {
  const newSet = new Set(selected.value);
  if (value) {
    newSet.add(key);
  } else {
    newSet.delete(key);
  }
  selected.value = newSet;
};

const toggleAll = (value: boolean) => {
  if (value) {
    selected.value = new Set(changedFields.value);
  } else {
    selected.value = new Set();
  }
};

const applySelected = () => {
  emit("apply", props.searchResult, selected.value);
};

const applyAll = () => {
  const allKeys = new Set(fields.value.map((f) => f.key));
  emit("apply", props.searchResult, allKeys);
};

const truncate = (str: string, length: number): string => {
  if (str.length <= length) return str;
  return str.substring(0, length) + "...";
};
</script>
