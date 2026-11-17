export type UpdateMechanism = 'builtin' | 'docker' | 'external' | 'script';

export interface UpdateMechanismOption {
  value: UpdateMechanism;
  label: string;
  help: string;
}

export const UPDATE_MECHANISM_OPTIONS: UpdateMechanismOption[] = [
  {
    value: 'builtin',
    label: 'Built-in',
    help: 'Sportarr downloads the new version, installs it and restarts. Not for Docker.',
  },
  {
    value: 'docker',
    label: 'Docker',
    help: 'Pull the new image and recreate the container.',
  },
  {
    value: 'external',
    label: 'External',
    help: 'Update with your installer or package manager. Sportarr shows when a new version is out.',
  },
  {
    value: 'script',
    label: 'Script',
    help: 'Sportarr runs your script when a new version is out and passes the version and download link.',
  },
];

// The server sends one of the four values. An unknown value shows as
// External, the same as the server reads it.
export function normalizeUpdateMechanism(value: string | null | undefined): UpdateMechanism {
  const lower = (value ?? '').trim().toLowerCase();
  if (lower === '') return 'docker';
  return UPDATE_MECHANISM_OPTIONS.some((o) => o.value === lower) ? (lower as UpdateMechanism) : 'external';
}

export function updateMechanismHelp(value: string): string {
  return UPDATE_MECHANISM_OPTIONS.find((o) => o.value === normalizeUpdateMechanism(value))?.help ?? '';
}

export function automaticUpdatesApply(value: string): boolean {
  const mechanism = normalizeUpdateMechanism(value);
  return mechanism === 'builtin' || mechanism === 'script';
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

// The Install button works from the saved mechanism. A choice that is not
// saved yet needs a save first.
export function installBlockReason(selectedMechanism: string, status: UpdateStatus | null): string | null {
  if (!status) return 'Checking for updates.';
  if (normalizeUpdateMechanism(status.mechanism) !== normalizeUpdateMechanism(selectedMechanism)) {
    return 'Save your settings first.';
  }
  return status.canInstall ? null : status.reason;
}
