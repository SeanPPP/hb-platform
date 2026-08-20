const MAX_SUMMARY_RETRIES = 3;
const SUMMARY_RETRY_BASE_MS = 2_000;

export function needsHostRemount(entry) {
  return !entry?.host?.isConnected;
}

export function resetSummaryRetry(entry) {
  entry.retryCount = 0;
  entry.nextRetryAt = 0;
}

export function markSummaryRequestFailed(entry, now = Date.now()) {
  const retryCount = (entry.retryCount || 0) + 1;
  const retryable = retryCount <= MAX_SUMMARY_RETRIES;
  const state = { kind: 'error', reason: 'error', retryable };
  entry.requested = false;
  entry.retryCount = retryCount;
  entry.nextRetryAt = retryable
    ? now + SUMMARY_RETRY_BASE_MS * (2 ** (retryCount - 1))
    : 0;
  entry.state = state;
  return state;
}

export function shouldRequestVisibleSummary(entry, now = Date.now()) {
  if (!entry || !entry.isVisible || entry.requested) return false;
  if (entry.state?.kind === 'loading') return true;
  return entry.state?.kind === 'error'
    && entry.state.retryable === true
    && (entry.nextRetryAt || 0) <= now;
}
