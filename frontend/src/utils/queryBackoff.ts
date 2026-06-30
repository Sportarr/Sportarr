const DEFAULT_MAX_BACKOFF_MS = 120_000;

let configuredMaxBackoffMs = DEFAULT_MAX_BACKOFF_MS;

function clampBackoffCap(value: number): number {
  if (!Number.isFinite(value)) {
    return DEFAULT_MAX_BACKOFF_MS;
  }

  const rounded = Math.round(value);
  return Math.min(Math.max(rounded, 1_000), 600_000);
}

export function setGlobalBackoffCap(maxIntervalMs: number): void {
  configuredMaxBackoffMs = clampBackoffCap(maxIntervalMs);
}

export function getGlobalBackoffCap(): number {
  return configuredMaxBackoffMs;
}

/**
 * Exponential backoff for polling/refetch timers.
 *
 * failureCount comes from React Query and resets to 0 after a successful fetch.
 */
export function getRefetchIntervalWithBackoff(
  baseIntervalMs: number,
  failureCount: number,
  maxIntervalMs: number = configuredMaxBackoffMs,
): number {
  const safeBase = Math.max(0, baseIntervalMs);
  const safeFailures = Math.max(0, failureCount);
  if (safeFailures === 0) {
    return safeBase;
  }

  const exponentialDelay = safeBase * 2 ** safeFailures;
  return Math.min(exponentialDelay, clampBackoffCap(maxIntervalMs));
}

export function getRetryDelayWithBackoff(attemptIndex: number): number {
  const safeAttemptIndex = Math.max(0, attemptIndex);
  const baseDelayMs = 1_000;
  const retryDelay = baseDelayMs * 2 ** (safeAttemptIndex + 1);
  return Math.min(retryDelay, configuredMaxBackoffMs);
}
