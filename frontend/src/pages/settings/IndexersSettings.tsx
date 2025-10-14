import { useState } from 'react';
import { PlusIcon, PencilIcon, TrashIcon, CheckCircleIcon, XCircleIcon, MagnifyingGlassIcon } from '@heroicons/react/24/outline';

interface IndexersSettingsProps {
  showAdvanced: boolean;
}

interface Indexer {
  id: number;
  name: string;
  implementation: string;
  protocol: 'usenet' | 'torrent';
  enabled: boolean;
  priority: number;
  apiKey?: string;
  baseUrl?: string;
  categories?: string[];
  minimumSeeders?: number;
  seedRatio?: number;
  seedTime?: number;
}

export default function IndexersSettings({ showAdvanced }: IndexersSettingsProps) {
  const [indexers, setIndexers] = useState<Indexer[]>([
    {
      id: 1,
      name: 'NZBgeek',
      implementation: 'Newznab',
      protocol: 'usenet',
      enabled: true,
      priority: 25,
      apiKey: '***************************',
      baseUrl: 'https://api.nzbgeek.info',
      categories: ['5000', '5030', '5040'],
    },
    {
      id: 2,
      name: 'TorrentLeech',
      implementation: 'TorrentLeech',
      protocol: 'torrent',
      enabled: true,
      priority: 20,
      apiKey: '***************************',
      minimumSeeders: 1,
      seedRatio: 1.0,
      seedTime: 1440,
    },
  ]);

  const [showAddModal, setShowAddModal] = useState(false);

  const indexerTemplates = [
    { name: 'Newznab', protocol: 'usenet', description: 'Generic Newznab indexer' },
    { name: 'Torznab', protocol: 'torrent', description: 'Generic Torznab indexer (Jackett/Prowlarr)' },
    { name: 'Nyaa', protocol: 'torrent', description: 'Nyaa.si anime torrent site' },
    { name: 'TorrentLeech', protocol: 'torrent', description: 'Private torrent tracker' },
    { name: 'IPTorrents', protocol: 'torrent', description: 'Private general tracker' },
    { name: 'FileList', protocol: 'torrent', description: 'Romanian private tracker' },
  ];

  return (
    <div className="max-w-6xl">
      <div className="mb-8">
        <h2 className="text-3xl font-bold text-white mb-2">Indexers</h2>
        <p className="text-gray-400">
          Configure Usenet indexers and torrent trackers for searching combat sports events
        </p>
      </div>

      {/* Info Box */}
      <div className="mb-8 bg-blue-950/30 border border-blue-900/50 rounded-lg p-6">
        <div className="flex items-start">
          <MagnifyingGlassIcon className="w-6 h-6 text-blue-400 mr-3 flex-shrink-0 mt-0.5" />
          <div>
            <h3 className="text-lg font-semibold text-white mb-2">About Indexers</h3>
            <ul className="space-y-2 text-sm text-gray-300">
              <li className="flex items-start">
                <span className="text-red-400 mr-2">•</span>
                <span>
                  <strong>Usenet Indexers:</strong> Newznab-compatible sites that index Usenet posts
                </span>
              </li>
              <li className="flex items-start">
                <span className="text-red-400 mr-2">•</span>
                <span>
                  <strong>Torrent Trackers:</strong> Sites or applications (Jackett/Prowlarr) that track
                  torrent files
                </span>
              </li>
              <li className="flex items-start">
                <span className="text-red-400 mr-2">•</span>
                <span>
                  <strong>Priority:</strong> Higher priority indexers are searched first (1-50)
                </span>
              </li>
              <li className="flex items-start">
                <span className="text-red-400 mr-2">•</span>
                <span>
                  Use <strong>Prowlarr</strong> or <strong>Jackett</strong> to manage multiple indexers
                  through a single Torznab endpoint
                </span>
              </li>
            </ul>
          </div>
        </div>
      </div>

      {/* Indexers List */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <div className="flex items-center justify-between mb-6">
          <h3 className="text-xl font-semibold text-white">Your Indexers</h3>
          <button
            onClick={() => setShowAddModal(true)}
            className="flex items-center px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
          >
            <PlusIcon className="w-4 h-4 mr-2" />
            Add Indexer
          </button>
        </div>

        <div className="space-y-3">
          {indexers.map((indexer) => (
            <div
              key={indexer.id}
              className="group bg-black/30 border border-gray-800 hover:border-red-900/50 rounded-lg p-4 transition-all"
            >
              <div className="flex items-start justify-between">
                <div className="flex items-start space-x-4 flex-1">
                  {/* Status Icon */}
                  <div className="mt-1">
                    {indexer.enabled ? (
                      <CheckCircleIcon className="w-6 h-6 text-green-500" />
                    ) : (
                      <XCircleIcon className="w-6 h-6 text-gray-500" />
                    )}
                  </div>

                  {/* Indexer Info */}
                  <div className="flex-1">
                    <div className="flex items-center space-x-3 mb-2">
                      <h4 className="text-lg font-semibold text-white">{indexer.name}</h4>
                      <span
                        className={`px-2 py-0.5 text-xs rounded ${
                          indexer.protocol === 'usenet'
                            ? 'bg-blue-900/30 text-blue-400'
                            : 'bg-green-900/30 text-green-400'
                        }`}
                      >
                        {indexer.protocol.toUpperCase()}
                      </span>
                      <span className="px-2 py-0.5 bg-gray-800 text-gray-400 text-xs rounded">
                        Priority: {indexer.priority}
                      </span>
                    </div>

                    <div className="space-y-1 text-sm text-gray-400">
                      <p>
                        <span className="text-gray-500">Implementation:</span>{' '}
                        <span className="text-white">{indexer.implementation}</span>
                      </p>
                      {indexer.baseUrl && (
                        <p>
                          <span className="text-gray-500">URL:</span>{' '}
                          <span className="text-white">{indexer.baseUrl}</span>
                        </p>
                      )}
                      {indexer.categories && indexer.categories.length > 0 && (
                        <p>
                          <span className="text-gray-500">Categories:</span>{' '}
                          <span className="text-white">{indexer.categories.join(', ')}</span>
                        </p>
                      )}
                      {indexer.protocol === 'torrent' && (
                        <div className="flex items-center space-x-4 mt-2">
                          {indexer.minimumSeeders !== undefined && (
                            <span className="text-gray-500">
                              Min Seeders:{' '}
                              <span className="text-white">{indexer.minimumSeeders}</span>
                            </span>
                          )}
                          {indexer.seedRatio !== undefined && (
                            <span className="text-gray-500">
                              Seed Ratio: <span className="text-white">{indexer.seedRatio}</span>
                            </span>
                          )}
                        </div>
                      )}
                    </div>
                  </div>
                </div>

                {/* Actions */}
                <div className="flex items-center space-x-2 ml-4">
                  <button className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors">
                    <CheckCircleIcon className="w-5 h-5" title="Test" />
                  </button>
                  <button className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors">
                    <PencilIcon className="w-5 h-5" />
                  </button>
                  <button className="p-2 text-gray-400 hover:text-red-400 hover:bg-red-950/30 rounded transition-colors">
                    <TrashIcon className="w-5 h-5" />
                  </button>
                </div>
              </div>
            </div>
          ))}
        </div>

        {indexers.length === 0 && (
          <div className="text-center py-12">
            <MagnifyingGlassIcon className="w-16 h-16 text-gray-700 mx-auto mb-4" />
            <p className="text-gray-500 mb-2">No indexers configured</p>
            <p className="text-sm text-gray-400 mb-4">
              Add indexers to search for combat sports events
            </p>
          </div>
        )}
      </div>

      {/* Indexer Options (Advanced) */}
      {showAdvanced && (
        <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-yellow-900/30 rounded-lg p-6">
          <h3 className="text-xl font-semibold text-white mb-4">
            Indexer Options
            <span className="ml-2 px-2 py-0.5 bg-yellow-900/30 text-yellow-400 text-xs rounded">
              Advanced
            </span>
          </h3>

          <div className="space-y-4">
            <div>
              <label className="block text-white font-medium mb-2">Retention</label>
              <div className="flex items-center space-x-2">
                <input
                  type="number"
                  defaultValue={0}
                  className="w-32 px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                />
                <span className="text-gray-400">days</span>
              </div>
              <p className="text-sm text-gray-400 mt-1">
                Set to 0 to disable. Releases older than this will not be grabbed
              </p>
            </div>

            <div>
              <label className="block text-white font-medium mb-2">RSS Sync Interval</label>
              <div className="flex items-center space-x-2">
                <input
                  type="number"
                  defaultValue={60}
                  className="w-32 px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                />
                <span className="text-gray-400">minutes</span>
              </div>
              <p className="text-sm text-gray-400 mt-1">
                How often Fightarr will sync with indexers. Minimum 10 minutes.
              </p>
            </div>

            <label className="flex items-start space-x-3 cursor-pointer">
              <input
                type="checkbox"
                defaultChecked={true}
                className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
              />
              <div>
                <span className="text-white font-medium">Prefer Indexer Flags</span>
                <p className="text-sm text-gray-400 mt-1">
                  Prefer releases with special indexer flags (Freeleech, Scene, etc.)
                </p>
              </div>
            </label>
          </div>
        </div>
      )}

      {/* Save Button */}
      <div className="flex justify-end">
        <button className="px-6 py-3 bg-gradient-to-r from-red-600 to-red-700 hover:from-red-700 hover:to-red-800 text-white font-semibold rounded-lg shadow-lg transform transition hover:scale-105">
          Save Changes
        </button>
      </div>

      {/* Add Indexer Modal */}
      {showAddModal && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-3xl w-full max-h-[80vh] overflow-y-auto">
            <h3 className="text-2xl font-bold text-white mb-4">Add Indexer</h3>
            <p className="text-gray-400 mb-6">Select an indexer type to configure</p>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-3 mb-6">
              {indexerTemplates.map((template, index) => (
                <button
                  key={index}
                  className="flex items-start p-4 bg-black/30 border border-gray-800 hover:border-red-600 rounded-lg transition-all text-left group"
                >
                  <div className="flex-1">
                    <div className="flex items-center space-x-2 mb-1">
                      <h4 className="text-white font-semibold">{template.name}</h4>
                      <span
                        className={`px-2 py-0.5 text-xs rounded ${
                          template.protocol === 'usenet'
                            ? 'bg-blue-900/30 text-blue-400'
                            : 'bg-green-900/30 text-green-400'
                        }`}
                      >
                        {template.protocol.toUpperCase()}
                      </span>
                    </div>
                    <p className="text-sm text-gray-400">{template.description}</p>
                  </div>
                  <PlusIcon className="w-5 h-5 text-gray-400 group-hover:text-red-400 transition-colors" />
                </button>
              ))}
            </div>

            <div className="flex items-center justify-end space-x-3 pt-4 border-t border-gray-800">
              <button
                onClick={() => setShowAddModal(false)}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
