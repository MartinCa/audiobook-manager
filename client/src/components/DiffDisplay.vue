<template>
  <div class="diff-display">
    <div class="diff-label">Expected:</div>
    <div class="diff-content">
      <template
        v-for="(part, i) in diffParts"
        :key="i"
      >
        <span
          v-if="!part.removed"
          :class="{ 'diff-highlight': part.added }"
          >{{ part.value }}</span
        >
      </template>
    </div>
    <div class="diff-label">Actual:</div>
    <div class="diff-content">
      <template
        v-for="(part, i) in diffParts"
        :key="i"
      >
        <span
          v-if="!part.added"
          :class="{ 'diff-highlight': part.removed }"
          >{{ part.value }}</span
        >
      </template>
    </div>
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
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 0 0.5em;
  font-family: monospace;
  font-size: 0.85em;
}

.diff-label {
  font-weight: bold;
  white-space: nowrap;
}

.diff-content {
  word-break: break-all;
}

.diff-highlight {
  background-color: rgba(255, 193, 7, 0.4);
  border-radius: 2px;
}
</style>
