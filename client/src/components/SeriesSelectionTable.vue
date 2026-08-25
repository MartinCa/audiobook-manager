<template>
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
          <tr v-for="(s, idx) in series">
            <td>
              {{ s.seriesName }}
            </td>
            <td>
              {{ s.seriesPart }}
            </td>
            <td>
              <v-btn
                color="primary"
                @click="$emit('seriesChosen', idx)"
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

<script setup lang="ts">
import { BookSeriesSearchResult } from "../types/BookSearchResult";

defineProps<{ series: BookSeriesSearchResult[] }>();
defineEmits<{
  (e: "seriesChosen", idx: number): void;
}>();
</script>
