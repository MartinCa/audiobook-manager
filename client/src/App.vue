<!-- <template>
  <v-app>
    <v-main>
      <HelloWorld />
    </v-main>
  </v-app>
</template>

<script setup lang="ts">
  import HelloWorld from '@/components/HelloWorld.vue'
</script> -->

<template>
  <v-app>
    <v-app-bar>
      <v-app-bar-nav-icon
        v-if="mobile"
        @click="drawerOpen = !drawerOpen"
      />
      <v-app-bar-title
        v-if="mobile"
        style="flex: 0 1 auto"
        >Audiobook Manager</v-app-bar-title
      >
      <v-spacer />
      <LibrarySearch />
      <v-spacer />
    </v-app-bar>

    <v-navigation-drawer
      v-model="drawerOpen"
      :permanent="!mobile"
      :rail="!mobile"
      :expand-on-hover="!mobile"
      :temporary="mobile"
    >
      <v-list
        v-model:opened="openedGroups"
        open-strategy="multiple"
        density="compact"
        nav
      >
        <!-- <v-list-item prepend-icon="mdi-book"
                     title="Organize audiobooks"
                     :to="{ path: '/' }"
                     value="myfiles"></v-list-item>
        <v-list-item prepend-icon="mdi-cog"
                     :to="{ path: '/settings' }"
                     title="Settings"
                     value="shared"></v-list-item> -->

        <template
          v-for="(link, i) in links"
          :key="i"
        >
          <v-list-item
            v-if="!link.subLinks"
            :to="link.to"
            :prepend-icon="link.icon"
          >
            <v-list-item-title v-text="link.text" />
          </v-list-item>
          <v-list-group
            v-else
            :value="link.text"
            class="nav-subgroup"
          >
            <template v-slot:activator="{ props }">
              <v-list-item
                v-bind="props"
                :to="link.to"
                :prepend-icon="link.icon"
                :title="link.text"
              />
            </template>

            <v-list-item
              v-for="(subLink, j) in link.subLinks"
              :key="j"
              :to="subLink.to"
              :prepend-icon="subLink.icon"
              :title="subLink.text"
              class="nav-subgroup__item"
            />
          </v-list-group>
        </template>
      </v-list>
    </v-navigation-drawer>
    <v-main>
      <ErrorNotifications
        :errors="errors"
        @error-dismissed="onErrorDismissed"
      />
      <router-view></router-view>
      <!-- <BookList /> -->
    </v-main>
  </v-app>
</template>

<script setup lang="ts">
import { ref, watch } from "vue";
import { useDisplay } from "vuetify";
import { useRoute } from "vue-router";
import ErrorNotifications from "./components/ErrorNotifications.vue";
import LibrarySearch from "./components/LibrarySearch.vue";
import { MenuLink } from "./types/MenuLink";
import { useErrors } from "./components/errors";

const { mobile } = useDisplay();
const drawerOpen = ref(!mobile.value);

const route = useRoute();
const openedGroups = ref<string[]>(
  route.path.startsWith("/library") ? ["Library"] : [],
);
watch(
  () => route.path,
  (path) => {
    if (
      path.startsWith("/library") &&
      !openedGroups.value.includes("Library")
    ) {
      openedGroups.value.push("Library");
    }
  },
);

const { errors, onErrorDismissed } = useErrors();

const links: MenuLink[] = [
  {
    to: "/",
    icon: "mdi-book",
    text: "Organize audiobooks",
  },
  {
    to: "/library",
    icon: "mdi-library",
    text: "Library",
    subLinks: [
      {
        to: "/library/discovered",
        icon: "mdi-file-find",
        text: "Discovered",
      },
      {
        to: "/library/series",
        icon: "mdi-bookshelf",
        text: "Series",
      },
      {
        to: "/library/consistency",
        icon: "mdi-check-decagram",
        text: "Consistency",
      },
      {
        to: "/library/similar-values",
        icon: "mdi-set-merge",
        text: "Similar Values",
      },
      {
        to: "/library/missing-tags",
        icon: "mdi-tag-off",
        text: "Missing Tags",
      },
    ],
  },
  {
    icon: "mdi-cog",
    text: "Settings",
    to: "/settings",
  },
];
</script>

<style scoped>
/* Vuetify auto-opens a group whose child route is active, even while the
   drawer is collapsed to its icon-only rail (expand-on-hover only widens
   it on actual hover). Rendering the open children into that 56px rail
   has nowhere to go, so it draws as a stray, unlabeled flyout. The `rail`
   prop/model doesn't reliably reflect that in a v-model we can gate on
   (hover flips it independently of our state), so hide the children with
   CSS keyed off Vuetify's own `--rail` class instead — authoritative and
   always in sync with what's actually rendered. */
.v-navigation-drawer--rail:not(.v-navigation-drawer--is-hovering)
  .nav-subgroup
  :deep(.v-list-group__items) {
  display: none;
}

/* Sub-items of an expanded nav group (e.g. "Library") get their own
   indented lane instead of sitting flush with the parent item. */
.nav-subgroup :deep(.v-list-group__items) {
  position: relative;
  margin-left: 21px;
  padding-left: 20px;
  border-left: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
}

.nav-subgroup__item {
  min-height: 34px;
  font-size: 0.8125rem;
}

.nav-subgroup__item :deep(.v-list-item__prepend > .v-icon) {
  font-size: 16px;
  opacity: 0.75;
}

.nav-subgroup__item.v-list-item--active :deep(.v-list-item__prepend > .v-icon) {
  opacity: 1;
}

/* Mark the active sub-item on the rail line itself, rather than relying
   only on Vuetify's default flat highlight. */
.nav-subgroup__item.v-list-item--active::after {
  content: "";
  position: absolute;
  left: -21px;
  top: 50%;
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: rgb(var(--v-theme-primary));
  opacity: 1;
  transform: translateY(-50%);
}
</style>
