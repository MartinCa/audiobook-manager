<template>
  <v-row>
    <v-col>
      <v-expansion-panels v-model="activePanel">
        <v-expansion-panel
          v-for="(mappings, key) in groupedSeriesMappings"
          :key="mappings.mappedSeries"
        >
          <v-expansion-panel-title>
            <v-row>
              <v-col>
                {{ mappings.mappedSeries }}
              </v-col>
            </v-row>
          </v-expansion-panel-title>
          <v-expansion-panel-text>
            <SeriesMappingInput
              v-for="(mapping, i) in mappings.mappings"
              :key="mapping.id"
              :mapping="mapping"
              @mapping-deleted="mappingDeleted"
              @mapping-updated="mappingUpdated"
            />
          </v-expansion-panel-text>
        </v-expansion-panel>
      </v-expansion-panels>
    </v-col>
  </v-row>
  <v-row>
    <v-col>
      <v-btn
        color="primary"
        dark
        @click="showCreateDialog = true"
      >
        <v-icon>mdi-plus</v-icon>
      </v-btn>
    </v-col>
  </v-row>

  <v-dialog
    v-model="showCreateDialog"
    :width="dialogWidth"
    :fullscreen="mdAndDown"
  >
    <v-card :width="dialogWidth">
      <v-toolbar
        dark
        prominent
      >
      </v-toolbar>
      <v-card-text>
        <SeriesMappingInput
          @mapping-deleted="showCreateDialog = false"
          @mapping-updated="createMapping"
        />
      </v-card-text>
    </v-card>
  </v-dialog>
</template>

<script setup lang="ts">
import { onMounted, ref, Ref, computed } from "vue";
import {
  GroupMapping,
  SeriesMapping,
  SeriesMappingBase,
} from "../../types/SeriesMapping";
import SettingsService from "../../services/SettingsService";
import SeriesMappingInput from "./SeriesMappingInput.vue";
import { useDialogWidth } from "../dialog";

const activePanel: Ref<any> = ref(null);
const seriesMappings: Ref<SeriesMapping[]> = ref([]);
const showCreateDialog: Ref<boolean> = ref(false);

const { dialogWidth, mdAndDown } = useDialogWidth();

function OrderByArray<T, K extends keyof T>(values: T[], orderType: K) {
  return values.sort((a, b) =>
    a[orderType] > b[orderType] ? 1 : a[orderType] < b[orderType] ? -1 : 0,
  );
}

const groupedSeriesMappings = computed((): GroupMapping[] => {
  const mappingObject = seriesMappings.value.reduce<{
    [m: string]: SeriesMapping[];
  }>(
    (r, v, i, a, k = v.mappedSeries) => ((r[k] || (r[k] = [])).push(v), r),
    {},
  );

  return OrderByArray(
    Object.entries(mappingObject).map((v) => ({
      mappedSeries: v[0],
      mappings: v[1],
    })),
    "mappedSeries",
  );
});

const mappingDeleted = (mapping: SeriesMapping | undefined) => {
  if (mapping) {
    seriesMappings.value = seriesMappings.value.filter(
      (sm) => sm.id != mapping.id,
    );
  }
};

const mappingUpdated = (mapping: SeriesMapping) => {
  const idx = seriesMappings.value.findIndex((sm) => sm.id == mapping.id);
  seriesMappings.value[idx] = mapping;
};

const createMapping = async (mapping: SeriesMappingBase) => {
  const newMapping = await SettingsService.createSeriesMapping(mapping);
  seriesMappings.value.push(newMapping);
  showCreateDialog.value = false;
};

onMounted(async () => {
  const mappings = await SettingsService.getSeriesMappings();
  seriesMappings.value = mappings;
});
</script>
