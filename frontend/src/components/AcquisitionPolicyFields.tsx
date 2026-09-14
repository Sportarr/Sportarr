import { acquisitionPresets, type AcquisitionPolicy } from '../utils/acquisitionPolicy';

export default function AcquisitionPolicyFields({ value, onChange }: {
  value: AcquisitionPolicy;
  onChange: (value: AcquisitionPolicy) => void;
}) {
  const preset = Object.entries(acquisitionPresets).find(([, candidate]) =>
    Object.keys(candidate).every(key => candidate[key as keyof AcquisitionPolicy] === value[key as keyof AcquisitionPolicy]))?.[0] ?? 'custom';
  const inputClass = 'w-full px-3 py-2 bg-black border border-red-900/30 rounded-lg text-white focus:outline-none focus:border-red-600';

  return (
    <fieldset className="mb-6 space-y-3 min-w-0">
      <legend className="text-sm font-medium text-gray-300 mb-2">Automatic Acquisition</legend>
      <label className="block text-sm text-gray-300">
        Preset
        <select aria-label="Acquisition preset" className={`${inputClass} mt-1`} value={preset}
          onChange={event => { const selected = acquisitionPresets[event.target.value]; if (selected) onChange({ ...selected }); }}>
          <option value="unlimited">Unlimited</option>
          <option value="recent">Recent 14 Days</option>
          <option value="archive">Archive</option>
          <option value="manual">Manual Only</option>
          <option value="custom">Custom</option>
        </select>
      </label>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <div className="space-y-2">
          <label className="flex items-center gap-2 text-sm text-gray-300">
            <input type="checkbox" checked={value.automaticMissingEnabled}
              onChange={event => onChange({ ...value, automaticMissingEnabled: event.target.checked })} />
            Automatic missing downloads
          </label>
          <label className="block text-sm text-gray-300">
            Missing event age (days; 0 = unlimited)
            <input aria-label="Missing event age" type="number" min={0} max={2147483647} step={1}
              className={`${inputClass} mt-1`} disabled={!value.automaticMissingEnabled}
              value={value.automaticMissingMaxAgeDays}
              onChange={event => onChange({ ...value, automaticMissingMaxAgeDays: Math.min(2147483647, Math.max(0, Math.trunc(Number(event.target.value) || 0))) })} />
          </label>
        </div>
        <div className="space-y-2">
          <label className="flex items-center gap-2 text-sm text-gray-300">
            <input type="checkbox" checked={value.automaticUpgradesEnabled}
              onChange={event => onChange({ ...value, automaticUpgradesEnabled: event.target.checked })} />
            Automatic upgrades
          </label>
          <label className="block text-sm text-gray-300">
            Upgrade event age (days; 0 = unlimited)
            <input aria-label="Upgrade event age" type="number" min={0} max={2147483647} step={1}
              className={`${inputClass} mt-1`} disabled={!value.automaticUpgradesEnabled}
              value={value.automaticUpgradeMaxAgeDays}
              onChange={event => onChange({ ...value, automaticUpgradeMaxAgeDays: Math.min(2147483647, Math.max(0, Math.trunc(Number(event.target.value) || 0))) })} />
          </label>
        </div>
      </div>
    </fieldset>
  );
}