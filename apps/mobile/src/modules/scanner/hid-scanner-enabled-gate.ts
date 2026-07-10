export function createHidScannerEnabledGate(initialEnabled = true) {
  let enabled = initialEnabled;
  let revision = 0;

  return {
    setEnabled(nextEnabled: boolean) {
      if (nextEnabled === enabled) {
        return;
      }
      enabled = nextEnabled;
      revision += 1;
    },
    isEnabled() {
      return enabled;
    },
    captureSubmission() {
      const capturedRevision = revision;
      return () => enabled && capturedRevision === revision;
    },
  };
}
