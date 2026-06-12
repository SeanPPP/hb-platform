interface ResumeHiddenInputFocusOptions {
  refocus?: boolean;
}

export function createHiddenInputFocusController(focus: () => void) {
  let paused = false;

  return {
    focusIfAllowed() {
      if (paused) {
        return false;
      }
      focus();
      return true;
    },
    pauseHiddenInputFocus() {
      paused = true;
    },
    resumeHiddenInputFocus(options: ResumeHiddenInputFocusOptions = {}) {
      paused = false;
      if (options.refocus === false) {
        return false;
      }
      focus();
      return true;
    },
    isHiddenInputFocusPaused() {
      return paused;
    },
  };
}

export type HiddenInputFocusController = ReturnType<typeof createHiddenInputFocusController>;
