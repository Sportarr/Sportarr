import { useState } from 'react';
import { InformationCircleIcon } from '@heroicons/react/24/outline';

interface QualitySettingsProps {
  showAdvanced: boolean;
}

interface QualityDefinition {
  id: string;
  title: string;
  minSize: number;
  maxSize: number;
  preferredSize: number;
}

export default function QualitySettings({ showAdvanced }: QualitySettingsProps) {
  const [qualityDefinitions, setQualityDefinitions] = useState<QualityDefinition[]>([
    { id: 'bluray-2160p', title: 'Bluray-2160p', minSize: 35, maxSize: 400, preferredSize: 95 },
    { id: 'webdl-2160p', title: 'WEBDL-2160p', minSize: 35, maxSize: 400, preferredSize: 95 },
    { id: 'webrip-2160p', title: 'WEBRip-2160p', minSize: 35, maxSize: 400, preferredSize: 95 },
    { id: 'bluray-1080p', title: 'Bluray-1080p', minSize: 15, maxSize: 100, preferredSize: 30 },
    { id: 'webdl-1080p', title: 'WEBDL-1080p', minSize: 10, maxSize: 100, preferredSize: 25 },
    { id: 'webrip-1080p', title: 'WEBRip-1080p', minSize: 10, maxSize: 100, preferredSize: 25 },
    { id: 'bluray-720p', title: 'Bluray-720p', minSize: 8, maxSize: 60, preferredSize: 15 },
    { id: 'webdl-720p', title: 'WEBDL-720p', minSize: 5, maxSize: 60, preferredSize: 12 },
    { id: 'webrip-720p', title: 'WEBRip-720p', minSize: 5, maxSize: 60, preferredSize: 12 },
    { id: 'bluray-480p', title: 'Bluray-480p', minSize: 2, maxSize: 30, preferredSize: 8 },
    { id: 'webdl-480p', title: 'WEBDL-480p', minSize: 2, maxSize: 30, preferredSize: 6 },
    { id: 'dvd', title: 'DVD', minSize: 2, maxSize: 25, preferredSize: 6 },
  ]);

  const handleQualityChange = (id: string, field: 'minSize' | 'maxSize' | 'preferredSize', value: number) => {
    setQualityDefinitions((prev) =>
      prev.map((q) => (q.id === id ? { ...q, [field]: value } : q))
    );
  };

  const loadTrashGuidesSizes = () => {
    // Example Trash Guides recommended sizes
    const trashGuidesDefaults: Record<string, Partial<QualityDefinition>> = {
      'bluray-2160p': { minSize: 35, maxSize: 400, preferredSize: 95 },
      'webdl-2160p': { minSize: 35, maxSize: 400, preferredSize: 95 },
      'bluray-1080p': { minSize: 15, maxSize: 100, preferredSize: 35 },
      'webdl-1080p': { minSize: 10, maxSize: 100, preferredSize: 28 },
      'bluray-720p': { minSize: 8, maxSize: 60, preferredSize: 18 },
      'webdl-720p': { minSize: 5, maxSize: 60, preferredSize: 15 },
    };

    setQualityDefinitions((prev) =>
      prev.map((q) => ({
        ...q,
        ...(trashGuidesDefaults[q.id] || {}),
      }))
    );
  };

  return (
    <div className="max-w-6xl">
      <div className="mb-8">
        <h2 className="text-3xl font-bold text-white mb-2">Quality Definitions</h2>
        <p className="text-gray-400">
          Quality settings control file size limits for each quality level
        </p>
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

      {/* Trash Guides Button */}
      <div className="mb-6 flex items-center justify-between">
        <p className="text-sm text-gray-400">
          Sizes are per hour of content. Adjust based on your preferences and storage capacity.
        </p>
        <button
          onClick={loadTrashGuidesSizes}
          className="px-4 py-2 bg-gradient-to-r from-purple-600 to-purple-700 hover:from-purple-700 hover:to-purple-800 text-white font-medium rounded-lg transition-all"
        >
          Load TRaSH Guides Recommended Sizes
        </button>
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
                      value={quality.maxSize}
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
                      {(quality.maxSize * 3).toFixed(1)} GB
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
