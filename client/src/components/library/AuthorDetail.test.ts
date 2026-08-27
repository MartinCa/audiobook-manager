import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createVuetify } from "vuetify";
import * as components from "vuetify/components";
import * as directives from "vuetify/directives";
import { reactive } from "vue";
import AuthorDetail from "./AuthorDetail.vue";
import BrowseService from "../../services/BrowseService";
import AuthorDetailType from "../../types/AuthorDetail";

const vuetify = createVuetify({ components, directives });

const route = reactive({ params: { authorId: "1" } });

vi.mock("vue-router", () => ({
  useRoute: () => route,
}));

vi.mock("../../services/BrowseService", () => ({
  default: { getAuthorDetail: vi.fn() },
}));

const mockedGetAuthorDetail = vi.mocked(BrowseService.getAuthorDetail);

function makeDetail(id: number, name: string): AuthorDetailType {
  return {
    author: { id, name, bookCount: 1 },
    series: [],
    standaloneBooks: [],
  };
}

function mountDetail() {
  return mount(AuthorDetail, {
    global: { plugins: [vuetify] },
  });
}

function flushPromises() {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

beforeEach(() => {
  vi.clearAllMocks();
  route.params.authorId = "1";
});

describe("AuthorDetail route param reactivity", () => {
  it("reloads author detail when navigating to a different authorId while the component instance persists", async () => {
    mockedGetAuthorDetail
      .mockResolvedValueOnce(makeDetail(1, "First Author"))
      .mockResolvedValueOnce(makeDetail(2, "Second Author"));

    const wrapper = mountDetail();
    await flushPromises();

    expect(mockedGetAuthorDetail).toHaveBeenCalledTimes(1);
    expect(mockedGetAuthorDetail).toHaveBeenCalledWith(1);
    expect(wrapper.text()).toContain("First Author");

    route.params.authorId = "2";
    await flushPromises();

    expect(mockedGetAuthorDetail).toHaveBeenCalledTimes(2);
    expect(mockedGetAuthorDetail).toHaveBeenCalledWith(2);
    expect(wrapper.text()).toContain("Second Author");
    expect(wrapper.text()).not.toContain("First Author");

    wrapper.unmount();
  });
});
