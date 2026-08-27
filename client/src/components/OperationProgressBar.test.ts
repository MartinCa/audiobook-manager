import { describe, it, expect } from "vitest";
import { mount } from "@vue/test-utils";
import { createVuetify } from "vuetify";
import * as components from "vuetify/components";
import * as directives from "vuetify/directives";
import OperationProgressBar from "./OperationProgressBar.vue";

const vuetify = createVuetify({ components, directives });

function mountBar(processed: number, total: number) {
  return mount(OperationProgressBar, {
    global: { plugins: [vuetify] },
    props: { processed, total },
  });
}

describe("OperationProgressBar", () => {
  it("renders the processed/total value in its content slot", () => {
    const wrapper = mountBar(37, 50);
    expect(wrapper.text()).toContain("37 / 50");
  });

  // Regression test: Vuetify's striped background pattern tiles at a fixed pixel size
  // matching the bar's height. When the filled width is narrower than one tile (true for
  // roughly the first 10% of most operations), only a fragment of the diagonal pattern is
  // visible, and its looping scroll animation reads as the stripe flipping back and forth
  // instead of scrolling smoothly. Never re-add `striped` without also solving that.
  it("does not use the striped variant, which visibly glitches at low percentages", () => {
    const wrapper = mountBar(1, 50);
    expect(wrapper.find(".v-progress-linear--striped").exists()).toBe(false);
  });
});
