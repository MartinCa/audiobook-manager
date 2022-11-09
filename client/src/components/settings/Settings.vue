<template>
    <v-container>
        <v-row class="text-center">
            <v-col>
                <v-card>
                    <v-tabs v-model="activeTab"
                            background-color="primary">
                        <v-tab value="one">Series Mapping</v-tab>
                        <!-- <v-tab value="two">Item Two</v-tab>
                    <v-tab value="three">Item Three</v-tab> -->
                    </v-tabs>

                    <v-card-text>
                        <v-window v-model="activeTab">
                            <v-window-item value="one">
                                <SeriesMapping />
                            </v-window-item>

                            <!-- <v-window-item value="two">
                                Two
                            </v-window-item>

                            <v-window-item value="three">
                                Three
                            </v-window-item> -->
                        </v-window>
                    </v-card-text>

                </v-card>
            </v-col>
        </v-row>
    </v-container>
</template>

<script setup lang="ts">import { Ref, ref } from 'vue';

import SeriesMapping from './SeriesMapping.vue';

const activeTab: Ref<string | null> = ref(null);

</script>
