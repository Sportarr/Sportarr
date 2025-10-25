import { useState, useEffect } from 'react';
import { InformationCircleIcon } from '@heroicons/react/24/outline';

interface QualitySettingsProps {
  showAdvanced: boolean;
}

interface QualityDefinition {
  id: number;
  quality: number;
  title: string;
  minSize: number;
  maxSize: number | null;
  preferredSize: number;
}

export default function QualitySettings({ showAdvanced }: QualitySettingsProps) {
  const [qualityDefinitions, setQualityDefinitions] = useState<QualityDefinition[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    loadQualityDefinitions();
  }, []);

  const loadQualityDefinitions = async () => {
    try {
      const response = await fetch('/api/qualitydefinition');
      if (response.ok) {
        const data = await response.json();
        setQualityDefinitions(data);
      }
    } catch (error) {
      console.error('Failed to load quality definitions:', error);
    } finally {
      setLoading(false);
    }
  };

  const handleQualityChange = (id: number, field: 'minSize' | 'maxSize' | 'preferredSize', value: number) => {
    setQualityDefinitions((prev) =>
      prev.map((q) => (q.id === id ? { ...q, [field]: value } : q))
    );
  };

  const handleSave = async () => {
    setSaving(true);
    try {
      const response = await fetch('/api/qualitydefinition/bulk', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(qualityDefinitions),
      });

      if (response.ok) {
        await loadQualityDefinitions();
      }
    } catch (error) {
      console.error('Failed to save quality definitions:', error);
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <div className="max-w-6xl mx-auto text-center py-12">
        <div className="text-gray-400">Loading quality definitions...</div>
      </div>
    );
  }

  return (
    <div className="max-w-6xl mx-auto">
      <div className="mb-8 flex items-center justify-between">
        <div>
          <h2 className="text-3xl font-bold text-white mb-2">Quality Definitions</h2>
          <p className="text-gray-400">
            Quality settings control file size limits for each quality level
          </p>
        </div>
        <button
          onClick={handleSave}
          disabled={saving}
          className="px-6 py-2 bg-red-600 hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed text-white rounded-lg transition-colors"
        >
          {saving ? 'Saving...' : 'Save Changes'}
        </button>
      </div>

      {/* Info Box */}
      <div className="mb-8 bg-blue-950/30 border border-blue-900/50 rounded-lg p-6">
        <div className="flex items-start">
          <InformationCircleIcon className="w-6 h-6 text-blue-400 mr-3 flex-shrink-0 mt-0.5" />
          <div>
            <h3 className="text-lg font-semibold text-white mb-2">About Quality Definitions</h3>
            <ul className="space-y-2 text-sm text-gray-300">
              <li className="flex items-start">
                <span className="text-red-400 mr-2">•</span>
                <span>
                  <strong>Min Size:</strong> Minimum file size per hour of content. Files smaller will be
                  rejected.
                </span>
              </li>
              <li className="flex items-start">
                <span className="text-red-400 mr-2">•</span>
                <span>
                  <strong>Max Size:</strong> Maximum file size per hour. Files larger will be rejected.
                </span>
              </li>
              <li className="flex items-start">
                <span className="text-red-400 mr-2">•</span>
                <span>
                  <strong>Preferred Size:</strong> Target size for upgrades. Fightarr will upgrade to releases
                  closer to this size.
                </span>
              </li>
              <li className="flex items-start">
                <span className="text-red-400 mr-2">•</span>
                <span>
                  Combat sports events typically range from 2-5 hours. A 3-hour UFC event at 25 GB/hr would be
                  75 GB total.
                </span>
              </li>
            </ul>
          </div>
        </div>
      </div>

      {/* Trash Guides Link */}
      <div className="mb-6 flex items-center justify-between">
        <p className="text-sm text-gray-400">
          Sizes are per hour of content. Adjust based on your preferences and storage capacity.
        </p>
        <a
          href="https://trash-guides.info/"
          target="_blank"
          rel="noopener noreferrer"
          className="px-4 py-2 bg-gradient-to-r from-purple-600 to-purple-700 hover:from-purple-700 hover:to-purple-800 text-white font-medium rounded-lg transition-all inline-flex items-center"
        >
          <svg className="w-4 h-4 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14" />
          </svg>
          Visit TRaSH Guides
        </a>
      </div>

      {/* Quality Definitions Table */}
      <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead>
              <tr className="bg-black/50 border-b border-red-900/30">
                <th className="px-6 py-4 text-left text-sm font-semibold text-white">Quality</th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-white">
                  Min Size
                  <span className="block text-xs text-gray-400 font-normal">(GB/hr)</span>
                </th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-white">
                  Preferred
                  <span className="block text-xs text-gray-400 font-normal">(GB/hr)</span>
                </th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-white">
                  Max Size
                  <span className="block text-xs text-gray-400 font-normal">(GB/hr)</span>
                </th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-white">
                  Range
                  <span className="block text-xs text-gray-400 font-normal">(3hr event)</span>
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-800">
              {qualityDefinitions.map((quality, index) => (
                <tr
                  key={quality.id}
                  className={index % 2 === 0 ? 'bg-black/20' : 'bg-black/10'}
                >
                  <td className="px-6 py-4">
                    <span className="text-white font-medium">{quality.title}</span>
                  </td>
                  <td className="px-6 py-4">
                    <input
                      type="number"
                      value={quality.minSize}
                      onChange={(e) =>
                        handleQualityChange(quality.id, 'minSize', Number(e.target.value))
                      }
                      className="w-24 px-3 py-2 bg-gray-800 border border-gray-700 rounded text-white text-sm focus:outline-none focus:border-red-600"
                      step="0.5"
                      min="0"
                    />
                  </td>
                  <td className="px-6 py-4">
                    <input
                      type="number"
                      value={quality.preferredSize}
                      onChange={(e) =>
                        handleQualityChange(quality.id, 'preferredSize', Number(e.target.value))
                      }
                      className="w-24 px-3 py-2 bg-gray-800 border border-gray-700 rounded text-white text-sm focus:outline-none focus:border-red-600"
                      step="0.5"
                      min="0"
                    />
                  </td>
                  <td className="px-6 py-4">
                    <input
                      type="number"
                      value={quality.maxSize ?? ''}
                      onChange={(e) =>
                        handleQualityChange(quality.id, 'maxSize', Number(e.target.value))
                      }
                      className="w-24 px-3 py-2 bg-gray-800 border border-gray-700 rounded text-white text-sm focus:outline-none focus:border-red-600"
                      step="0.5"
                      min="0"
                    />
                  </td>
                  <td className="px-6 py-4">
                    <span className="text-gray-400 text-sm">
                      {(quality.minSize * 3).toFixed(1)} -{' '}
                      {quality.maxSize ? (quality.maxSize * 3).toFixed(1) : '∞'} GB
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {/* Warning for Advanced Users */}
      {showAdvanced && (
        <div className="mt-6 p-4 bg-yellow-950/30 border border-yellow-900/50 rounded-lg">
          <p className="text-sm text-yellow-300">
            <strong>Advanced:</strong> These values affect all quality profiles. Changes here impact which
            releases are accepted across your entire library. Be careful when adjusting these settings.
          </p>
        </div>
      )}

      {/* Save Button */}
      <div className="mt-8 flex justify-end">
        <button className="px-6 py-3 bg-gradient-to-r from-red-600 to-red-700 hover:from-red-700 hover:to-red-800 text-white font-semibold rounded-lg shadow-lg transform transition hover:scale-105">
          Save Changes
        </button>
      </div>
    </div>
  );
}
