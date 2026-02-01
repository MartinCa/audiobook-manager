<template>
  <v-row>
    <v-col
      cols="12"
      md="6"
      lg="3"
    >
      <v-img
        v-if="base64Data"
        max-height="200"
        class="bg-grey-darken-2"
        transition="false"
        :src="`data:${mimeType};base64,${base64Data}`"
      ></v-img>
      <template v-else> No cover </template>
    </v-col>
    <v-col
      cols="12"
      md="6"
      lg="9"
    >
      <v-row>
        <v-col
          cols="12"
          md="9"
        >
          <v-text-field
            label="Image url"
            hide-details="auto"
            v-model="imgUrl"
          ></v-text-field>
        </v-col>
        <v-col
          cols="12"
          md="3"
        >
          <v-btn
            color="primary"
            size="large"
            block
            @click="loadImgFromUrl(imgUrl)"
          >
            Fetch
          </v-btn>
        </v-col>
      </v-row>
      <v-row>
        <v-col
          cols="12"
          md="9"
        >
          <v-file-input
            label="Cover image upload"
            hide-details="auto"
            accept="image/*"
            v-model="uploadedImg"
          ></v-file-input>
        </v-col>
        <v-col
          cols="12"
          md="3"
        >
          <v-btn
            color="primary"
            size="large"
            block
            @click="loadUploadedImg(uploadedImg)"
          >
            Upload
          </v-btn>
        </v-col>
      </v-row>
    </v-col>
  </v-row>
</template>

<script setup lang="ts">
import { ref } from "vue";
import ImageService from "../services/ImageService";

defineProps<{
  base64Data: string | undefined;
  mimeType: string | undefined;
}>();

const emit = defineEmits<{
  (
    e: "update:cover",
    base64Data: string | undefined,
    mimeType: string | undefined,
  ): void;
}>();

const imgUrl = ref("");
const uploadedImg = ref([]);

const loadImgFromUrl = async (url: string | undefined) => {
  if (!url) {
    emit("update:cover", undefined, undefined);
    return;
  }
  const cover = await ImageService.downloadImageFromUrl(url);
  emit("update:cover", cover.base64Data, cover.mimeType);
};

const loadUploadedImg = async (uploaded: File[]) => {
  const cover = await ImageService.readBase64ImageFromBlob(uploaded[0]);
  emit("update:cover", cover.base64Data, cover.mimeType);
};

defineExpose({ loadImgFromUrl });
</script>
