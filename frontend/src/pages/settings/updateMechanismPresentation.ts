// The server finds how this install updates. The page only shows it.
// docker: the container only reports a new version.
// native: the built-in update works here.
// blocked: a native install where the built-in update cannot work.
// script: config.xml names a custom update script.
export type UpdateView = 'docker' | 'native' | 'blocked' | 'script';

export interface UpdateMethodFields {
  mechanism?: string | null;
  automaticAvailable?: boolean;
}

export const DOCKER_UPDATE_TEXT = 'Pull the new image and recreate the container to update.';
export const SCRIPT_UPDATE_TEXT = 'A custom update script is in use.';

export function updateView(update: UpdateMethodFields): UpdateView {
  const mechanism = (update.mechanism ?? '').trim().toLowerCase();
  if (mechanism === 'script') return 'script';
  if (mechanism === 'builtin') return update.automaticAvailable === true ? 'native' : 'blocked';
  // Docker, and any value this page does not know, only report.
  return 'docker';
}

export interface UpdateStatus {
  currentVersion: string;
  latestVersion: string | null;
  available: boolean;
  mechanism: string;
  automatic: boolean;
  canInstall: boolean;
  reason: string | null;
  releaseUrl: string | null;
  installing: boolean;
  lastResult?: { status: string; version: string; message: string; time: string } | null;
  // The status call checks this computer again, so it wins over the
  // settings answer.
  automaticAvailable?: boolean;
  automaticUnavailableReason?: string | null;
}

// A new failure has a new time, even when its text is the same as before.
export function isNewFailure(status: UpdateStatus, failureTimeBefore: string | null): boolean {
  return !!status.lastResult && status.lastResult.time !== failureTimeBefore;
}

export const CANCELLED_UPDATE_TEXT = 'The update was not installed because Sportarr was busy.';
const RETRY_LATER_TEXT = ' It tries again later.';

// The text for the last update result. A cancel is not a failure. Only
// automatic updates try again on their own.
export function lastResultText(lastResult: NonNullable<UpdateStatus['lastResult']>, automatic: boolean): string {
  if (lastResult.status === 'Cancelled') {
    return automatic ? CANCELLED_UPDATE_TEXT + RETRY_LATER_TEXT : CANCELLED_UPDATE_TEXT;
  }
  return `Update to ${lastResult.version} failed. ${lastResult.message}`;
}

export function isCancelled(lastResult: UpdateStatus['lastResult']): boolean {
  return lastResult?.status === 'Cancelled';
}

export function installBlockReason(status: UpdateStatus | null): string | null {
  if (!status) return 'Checking for updates.';
  return status.canInstall ? null : status.reason;
}
