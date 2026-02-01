/**
 * main.ts
 *
 * Bootstraps Vuetify and other plugins then mounts the App`
 */

// Components
import App from "./App.vue";
const BookList = () => import("./components/BookList.vue");
const Settings = () => import("./components/settings/Settings.vue");
const BookLibrary = () => import("./components/BookLibrary.vue");
const AuthorsList = () => import("./components/library/AuthorsList.vue");
const AuthorDetail = () => import("./components/library/AuthorDetail.vue");
const SeriesDetail = () => import("./components/library/SeriesDetail.vue");
const LibraryConsistency = () => import("./components/LibraryConsistency.vue");

// Composables
import { createApp } from "vue";

// Plugins
import { registerPlugins } from "@/plugins";
import vuetify from "./plugins/vuetify";
import { createRouter, createWebHashHistory } from "vue-router";
import { VueSignalR } from "@quangdao/vue-signalr";

const app = createApp(App);

registerPlugins();

const routes = [
  { path: "/", component: BookList },
  { path: "/library", component: BookLibrary },
  { path: "/library/authors", component: AuthorsList },
  { path: "/library/authors/:authorId", component: AuthorDetail },
  { path: "/library/series/:seriesName", component: SeriesDetail },
  { path: "/library/consistency", component: LibraryConsistency },
  { path: "/settings", component: Settings },
];

const router = createRouter({
  history: createWebHashHistory(),
  routes,
});

app
  .use(router)
  .use(vuetify)
  .use(VueSignalR, {
    url: `${import.meta.env.VITE_BASE_URL}hubs/organize`,
    automaticReconnect: true,
  })
  .mount("#app");
