<template>
  <v-card
    v-if="!deleteTarget"
    :width="dialogWidth"
  >
    <v-toolbar
      dark
      prominent
    >
      <v-toolbar-title>Duplicate file at target location</v-toolbar-title>
      <v-btn
        icon
        dark
        @click="$emit('cancelled')"
      >
        <v-icon>mdi-close</v-icon>
      </v-btn>
    </v-toolbar>

    <v-card-text>
      <p class="mb-4">
        A file already exists where this book would be organized to. Choose
        which copy to keep.
      </p>

      <v-row>
        <v-col
          cols="12"
          sm="6"
        >
          <div class="text-subtitle-2">New file</div>
          <div class="text-caption text-medium-emphasis mb-1">
            {{ newPath }}
          </div>
          <div>{{ formatSize(newSizeInBytes) }}</div>
          <div v-if="newDurationInSeconds != null">
            {{ formatDuration(newDurationInSeconds) }}
          </div>
        </v-col>
        <v-col
          cols="12"
          sm="6"
        >
          <div class="text-subtitle-2">Existing file</div>
          <div class="text-caption text-medium-emphasis mb-1">
            {{ targetPath }}
          </div>
          <div v-if="existingSizeInBytes != null">
            {{ formatSize(existingSizeInBytes) }}
          </div>
          <div v-if="existingDurationInSeconds != null">
            {{ formatDuration(existingDurationInSeconds) }}
          </div>
        </v-col>
      </v-row>
    </v-card-text>

    <v-card-actions>
      <v-btn
        color="error"
        @click="deleteTarget = 'existing'"
      >
        Replace existing
      </v-btn>
      <v-btn
        color="error"
        @click="deleteTarget = 'new'"
      >
        Delete new file
      </v-btn>
      <v-spacer />
      <v-btn @click="$emit('cancelled')">Cancel</v-btn>
    </v-card-actions>
  </v-card>

  <BookDeleteDialog
    v-else
    :book-details="deleteCandidate!"
    :dialog-width="dialogWidth"
    @delete-book="onDeleteResult"
  />
</template>

<script setup lang="ts">
import { computed, ref } from "vue";
import { Audiobook } from "../types/Audiobook";
import BookDeleteDialog from "./BookDeleteDialog.vue";
import { formatDuration } from "../helpers/formatHelpers";

const props = defineProps<{
  newPath: string;
  newSizeInBytes: number;
  newDurationInSeconds?: number;
  targetPath: string;
  existingSizeInBytes?: number;
  existingDurationInSeconds?: number;
  dialogWidth?: string;
}>();

const emit = defineEmits<{
  (e: "existingDeleted"): void;
  (e: "newDeleted"): void;
  (e: "cancelled"): void;
}>();

const deleteTarget = ref<"existing" | "new" | null>(null);

const fileNameOf = (path: string) => path.split(/[\\/]/).pop() ?? path;

const deleteCandidate = computed<Audiobook | null>(() => {
  if (deleteTarget.value === "existing") {
    return {
      authors: [],
      narrators: [],
      genres: [],
      fileInfo: {
        fullPath: props.targetPath,
        fileName: fileNameOf(props.targetPath),
        sizeInBytes: props.existingSizeInBytes ?? 0,
      },
    };
  }
  if (deleteTarget.value === "new") {
    return {
      authors: [],
      narrators: [],
      genres: [],
      fileInfo: {
        fullPath: props.newPath,
        fileName: fileNameOf(props.newPath),
        sizeInBytes: props.newSizeInBytes,
      },
    };
  }
  return null;
});

const onDeleteResult = (deleted: boolean) => {
  const target = deleteTarget.value;
  deleteTarget.value = null;
  if (!deleted) {
    return;
  }
  if (target === "existing") {
    emit("existingDeleted");
  } else if (target === "new") {
    emit("newDeleted");
  }
};

const formatSize = (size: number) => `${(size / 1000000).toFixed(2)} MB`;
</script>
