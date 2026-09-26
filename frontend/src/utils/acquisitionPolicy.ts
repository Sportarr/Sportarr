export interface AcquisitionPolicy {
  automaticMissingEnabled: boolean;
  automaticUpgradesEnabled: boolean;
  automaticMissingMaxAgeDays: number;
  automaticUpgradeMaxAgeDays: number;
}

export const acquisitionPresets: Record<string, AcquisitionPolicy> = {
  unlimited: { automaticMissingEnabled: true, automaticUpgradesEnabled: true, automaticMissingMaxAgeDays: 0, automaticUpgradeMaxAgeDays: 0 },
  recent: { automaticMissingEnabled: true, automaticUpgradesEnabled: true, automaticMissingMaxAgeDays: 14, automaticUpgradeMaxAgeDays: 14 },
  archive: { automaticMissingEnabled: true, automaticUpgradesEnabled: true, automaticMissingMaxAgeDays: 0, automaticUpgradeMaxAgeDays: 14 },
  manual: { automaticMissingEnabled: false, automaticUpgradesEnabled: false, automaticMissingMaxAgeDays: 0, automaticUpgradeMaxAgeDays: 0 },
};

export function acquisitionPolicyFrom(source: Partial<AcquisitionPolicy>): AcquisitionPolicy {
  return {
    automaticMissingEnabled: source.automaticMissingEnabled ?? true,
    automaticUpgradesEnabled: source.automaticUpgradesEnabled ?? true,
    automaticMissingMaxAgeDays: source.automaticMissingMaxAgeDays ?? 0,
    automaticUpgradeMaxAgeDays: source.automaticUpgradeMaxAgeDays ?? 0,
  };
}