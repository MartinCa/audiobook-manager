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
    <v-navigation-drawer
      expand-on-hover
      rail
    >
      <v-list
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
          <!-- <v-list-group v-else
                        :value="true"
                        :prepend-icon="link.icon">
            <template v-slot:activator>
              <v-list-item-content>
                <v-list-item-title>{{ link.text }}</v-list-item-title>
              </v-list-item-content>
            </template>

            <v-list-item v-for="(subLink, i) in link.subLinks"
                         :key="i"
                         link
                         :prepend-icon="subLink.icon">
              <v-list-item-title v-text="subLink.text"></v-list-item-title>

               <v-list-item-icon>
                <v-icon v-text="subLink.icon"></v-icon>
              </v-list-item-icon>
            </v-list-item>

          </v-list-group> -->
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
import ErrorNotifications from "./components/ErrorNotifications.vue";
import { MenuLink } from "./types/MenuLink";
import { useErrors } from "./components/errors";

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
  },
  {
    to: "/library/consistency",
    icon: "mdi-check-decagram",
    text: "Consistency",
  },
  {
    icon: "mdi-cog",
    text: "Settings",
    to: "/settings",
    // subLinks: [
    //   {
    //     icon: "mdi-cog",
    //     text: "Series Mapping",
    //     to: "/settings"
    //   }
    // ]
  },
];
</script>
