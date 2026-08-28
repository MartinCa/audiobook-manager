import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createVuetify } from "vuetify";
import * as components from "vuetify/components";
import * as directives from "vuetify/directives";
import { createRouter, createMemoryHistory } from "vue-router";
import LibrarySearch from "./LibrarySearch.vue";
import LibrarySearchService from "../services/LibrarySearchService";
import LibrarySearchResult from "../types/LibrarySearchResult";

vi.mock("../services/LibrarySearchService", () => ({
  default: { searchLibrary: vi.fn() },
}));

const mockedSearchLibrary = vi.mocked(LibrarySearchService.searchLibrary);

const vuetify = createVuetify({ components, directives });

function makeResult(): LibrarySearchResult {
  return {
    books: [],
    authors: [
      { id: 1, name: "S. H. Jucha", bookCount: 5 },
      { id: 2, name: "S.H. Jucha", bookCount: 1 },
    ],
    series: [],
  };
}

function makeRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: "/", component: { template: "<div>home</div>" } },
      {
        path: "/library/authors/:authorId",
        component: { template: "<div>author</div>" },
      },
    ],
  });
}

async function mountSearch() {
  const router = makeRouter();
  router.push("/");
  await router.isReady();

  const wrapper = mount(LibrarySearch, {
    global: { plugins: [vuetify, router] },
    attachTo: document.body,
  });

  return { wrapper, router };
}

function flushPromises() {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("LibrarySearch results visibility after navigation", () => {
  it("closes the results dropdown once a result is clicked and navigation occurs", async () => {
    mockedSearchLibrary.mockResolvedValue(makeResult());

    const { wrapper, router } = await mountSearch();

    const input = wrapper.find("input");
    await input.setValue("Jucha");
    await new Promise((resolve) => setTimeout(resolve, 300));
    await wrapper.vm.$nextTick();

    expect(document.body.textContent).toContain("S. H. Jucha");
    expect(
      document.body.querySelector(".v-overlay.v-menu.v-overlay--active"),
    ).toBeTruthy();

    const authorLink = document.body.querySelector(
      'a[href="/library/authors/1"]',
    ) as HTMLAnchorElement;
    expect(authorLink).toBeTruthy();
    authorLink.click();

    await router.isReady();
    await flushPromises();
    await wrapper.vm.$nextTick();

    expect(
      document.body.querySelector(".v-overlay.v-menu.v-overlay--active"),
    ).toBeFalsy();
    expect((input.element as HTMLInputElement).value).toBe("");

    wrapper.unmount();
  });

  it("closes the results dropdown when navigation happens via any other means while open", async () => {
    mockedSearchLibrary.mockResolvedValue(makeResult());

    const { wrapper, router } = await mountSearch();

    const input = wrapper.find("input");
    await input.setValue("Jucha");
    await new Promise((resolve) => setTimeout(resolve, 300));
    await wrapper.vm.$nextTick();

    expect(
      document.body.querySelector(".v-overlay.v-menu.v-overlay--active"),
    ).toBeTruthy();

    await router.push("/library/authors/2");
    await flushPromises();
    await wrapper.vm.$nextTick();

    expect(
      document.body.querySelector(".v-overlay.v-menu.v-overlay--active"),
    ).toBeFalsy();

    wrapper.unmount();
  });
});
