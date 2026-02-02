<template>
  <div class="diff-display">
    <span
      v-for="(part, i) in diffParts"
      :key="i"
      :class="{
        'diff-added': part.added,
        'diff-removed': part.removed,
      }"
      >{{ part.value }}</span
    >
  </div>
</template>

<script setup lang="ts">
import { computed } from "vue";
import { diffChars, type Change } from "diff";

const props = defineProps<{
  expected: string;
  actual: string;
}>();

const diffParts = computed((): Change[] => {
  return diffChars(props.actual, props.expected);
});
</script>

<style scoped>
.diff-display {
  font-family: monospace;
  font-size: 0.85em;
  word-break: break-all;
}

.diff-added {
  background-color: rgba(76, 175, 80, 0.3);
  border-radius: 2px;
}

.diff-removed {
  background-color: rgba(244, 67, 54, 0.3);
  text-decoration: line-through;
  border-radius: 2px;
}
</style>
