// Global test environment setup for Vitest + jsdom.
//
// jsdom does not implement window.visualViewport, which Vuetify's VOverlay
// location strategies (used by v-dialog, v-menu, v-tooltip, etc.) read from.
// Without this, mounting any component that opens a Vuetify overlay throws
// "visualViewport is not defined".
if (typeof window !== "undefined" && !window.visualViewport) {
  Object.defineProperty(window, "visualViewport", {
    writable: true,
    configurable: true,
    value: {
      width: window.innerWidth,
      height: window.innerHeight,
      addEventListener: () => {},
      removeEventListener: () => {},
    },
  });
}

// jsdom also does not implement ResizeObserver, which several Vuetify
// components (VSlideGroup/VChipGroup among them) rely on.
if (typeof window !== "undefined" && !("ResizeObserver" in window)) {
  class ResizeObserverStub {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
  // @ts-expect-error - minimal stub, not a full ResizeObserver implementation
  window.ResizeObserver = ResizeObserverStub;
  // @ts-expect-error
  globalThis.ResizeObserver = ResizeObserverStub;
}
