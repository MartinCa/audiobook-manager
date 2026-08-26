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
    <v-app-bar v-if="mobile">
      <v-app-bar-nav-icon @click="drawerOpen = !drawerOpen" />
      <v-app-bar-title>Audiobook Manager</v-app-bar-title>
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
