<template>
  <v-form
    ref="form"
    v-if="input"
  >
    <v-row>
      <v-col>
        <v-text-field
          density="comfortable"
          v-model="input.mappedSeries"
          :rules="[(v) => !!v || 'Mapped series is required']"
          label="Mapped series"
          hide-details="auto"
        >
        </v-text-field>
      </v-col>
      <v-col>
        <v-text-field
          density="comfortable"
          v-model="input.regex"
          :rules="[(v) => !!v || 'Regex is required']"
          label="Regex"
          hide-details="auto"
        >
        </v-text-field>
      </v-col>
      <v-col>
        <v-switch
          v-model="input.warnAboutPart"
          color="primary"
          hide-details="auto"
          label="Warn about part"
        ></v-switch>
      </v-col>
      <v-col>
        <v-btn
          icon
          title="Save"
          @click="updateMapping"
        >
          <v-icon>mdi-check</v-icon>
        </v-btn>
        <v-btn
          icon
          title="Delete"
          @click="deleteMapping"
        >
          <v-icon>mdi-delete</v-icon>
        </v-btn>
      </v-col>
    </v-row>
  </v-form>
</template>

<script setup lang="ts">
import { onMounted, Ref, ref } from "vue";
import { SeriesMapping } from "../../types/SeriesMapping";
import SettingsService from "../../services/SettingsService";

const form: Ref<any | null> = ref(null);

const props = defineProps<{ mapping?: SeriesMapping }>();

const input: Ref<SeriesMapping | undefined> = ref(undefined);

const emit = defineEmits<{
  (e: "mappingDeleted", mapping: SeriesMapping | undefined): void;
  (e: "mappingUpdated", updatedMapping: SeriesMapping): void;
}>();

onMounted(() => {
  input.value = Object.assign(
    { regex: "", mapped_series: "", warn_about_part: false },
    props.mapping
  );
});

const deleteMapping = async () => {
  if (props.mapping) {
    await SettingsService.deleteSeriesMapping(props.mapping.id);
  }

  emit("mappingDeleted", props.mapping);
};

const validateForm = async (): Promise<boolean> => {
  if (!form) {
    return false;
  }

  const formValidation = await form.value.validate();

  return formValidation.valid;
};

const updateMapping = async () => {
  const formValid = await validateForm();

  if (!formValid) {
    return;
  }

  let updatedMapping: SeriesMapping | undefined = input.value;

  if (props.mapping && input.value) {
    updatedMapping = await SettingsService.updateSeriesMapping(input.value);
  }

  if (updatedMapping) {
    emit("mappingUpdated", updatedMapping);
  }
};
</script>
