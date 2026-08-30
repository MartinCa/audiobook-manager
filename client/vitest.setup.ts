import "@testing-library/jest-dom";

// jsdom doesn't implement matchMedia; ThemeProvider (dark-mode detection) needs it whenever a
// test renders the app's root layout or anything wrapped in ThemeProvider.
if (typeof window !== "undefined" && !window.matchMedia) {
  window.matchMedia = (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  });
}
