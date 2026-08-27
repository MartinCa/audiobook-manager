<template>
  <v-progress-linear
    :model-value="total > 0 ? (processed / total) * 100 : 0"
    color="primary"
    height="20"
    striped
  >
    <template v-slot:default> {{ processed }} / {{ total }} </template>
  </v-progress-linear>
</template>

<script setup lang="ts">
defineProps<{
  processed: number;
  total: number;
}>();
</script>
