import { useState } from 'react';
import { PlusIcon, PencilIcon, TrashIcon, CheckCircleIcon, XCircleIcon, ArrowDownTrayIcon } from '@heroicons/react/24/outline';

interface DownloadClientsSettingsProps {
  showAdvanced: boolean;
}

interface DownloadClient {
  id: number;
  name: string;
  implementation: string;
  protocol: 'usenet' | 'torrent';
  enabled: boolean;
  priority: number;
  host: string;
  port: number;
  username?: string;
  category?: string;
  recentPriority?: string;
  olderPriority?: string;
}

export default function DownloadClientsSettings({ showAdvanced }: DownloadClientsSettingsProps) {
  const [downloadClients, setDownloadClients] = useState<DownloadClient[]>([
    {
      id: 1,
      name: 'SABnzbd',
      implementation: 'Sabnzbd',
      protocol: 'usenet',
      enabled: true,
      priority: 1,
      host: 'localhost',
      port: 8080,
      category: 'fightarr',
      recentPriority: 'Default',
      olderPriority: 'Default',
    },
    {
      id: 2,
      name: 'qBittorrent',
      implementation: 'QBittorrent',
      protocol: 'torrent',
      enabled: true,
      priority: 1,
      host: 'localhost',
      port: 8081,
      username: 'admin',
      category: 'fightarr',
    },
  ]);

  const [showAddModal, setShowAddModal] = useState(false);

  // Completed Download Handling
  const [enableCompletedDownloadHandling, setEnableCompletedDownloadHandling] = useState(true);
  const [removeCompletedDownloads, setRemoveCompletedDownloads] = useState(true);
  const [checkForFinishedDownloads, setCheckForFinishedDownloads] = useState(1);

  // Failed Download Handling
  const [enableFailedDownloadHandling, setEnableFailedDownloadHandling] = useState(true);
  const [removeFailedDownloads, setRemoveFailedDownloads] = useState(true);
  const [redownloadFailedEvents, setRedownloadFailedEvents] = useState(true);

  const downloadClientTemplates = [
    { name: 'SABnzbd', protocol: 'usenet', description: 'Open source binary newsreader' },
    { name: 'NZBGet', protocol: 'usenet', description: 'Efficient Usenet downloader' },
    { name: 'qBittorrent', protocol: 'torrent', description: 'Free and reliable torrent client' },
    { name: 'Transmission', protocol: 'torrent', description: 'Fast and easy torrent client' },
    { name: 'Deluge', protocol: 'torrent', description: 'Lightweight torrent client' },
    { name: 'rTorrent', protocol: 'torrent', description: 'Command-line torrent client' },
    { name: 'Vuze', protocol: 'torrent', description: 'Feature-rich torrent client' },
  ];

  return (
    <div className="max-w-6xl">
      <div className="mb-8">
        <h2 className="text-3xl font-bold text-white mb-2">Download Clients</h2>
        <p className="text-gray-400">
          Configure download clients for Usenet and torrent downloads
        </p>
      </div>

      {/* Info Box */}
      <div className="mb-8 bg-blue-950/30 border border-blue-900/50 rounded-lg p-6">
        <div className="flex items-start">
          <ArrowDownTrayIcon className="w-6 h-6 text-blue-400 mr-3 flex-shrink-0 mt-0.5" />
          <div>
            <h3 className="text-lg font-semibold text-white mb-2">About Download Clients</h3>
            <ul className="space-y-2 text-sm text-gray-300">
              <li className="flex items-start">
                <span className="text-red-400 mr-2">•</span>
                <span>
                  <strong>Usenet Clients:</strong> SABnzbd, NZBGet for downloading from Usenet
                </span>
              </li>
              <li className="flex items-start">
                <span className="text-red-400 mr-2">•</span>
                <span>
                  <strong>Torrent Clients:</strong> qBittorrent, Transmission, Deluge for torrents
                </span>
              </li>
              <li className="flex items-start">
                <span className="text-red-400 mr-2">•</span>
                <span>
                  <strong>Priority:</strong> Lower priority clients are used as fallback
                </span>
              </li>
              <li className="flex items-start">
                <span className="text-red-400 mr-2">•</span>
                <span>
                  Use <strong>categories</strong> to organize downloads and allow proper hardlinking
                </span>
              </li>
            </ul>
          </div>
        </div>
      </div>

      {/* Download Clients List */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <div className="flex items-center justify-between mb-6">
          <h3 className="text-xl font-semibold text-white">Your Download Clients</h3>
          <button
            onClick={() => setShowAddModal(true)}
            className="flex items-center px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
          >
            <PlusIcon className="w-4 h-4 mr-2" />
            Add Download Client
          </button>
        </div>

        <div className="space-y-3">
          {downloadClients.map((client) => (
            <div
              key={client.id}
              className="group bg-black/30 border border-gray-800 hover:border-red-900/50 rounded-lg p-4 transition-all"
            >
              <div className="flex items-start justify-between">
                <div className="flex items-start space-x-4 flex-1">
                  {/* Status Icon */}
                  <div className="mt-1">
                    {client.enabled ? (
                      <CheckCircleIcon className="w-6 h-6 text-green-500" />
                    ) : (
                      <XCircleIcon className="w-6 h-6 text-gray-500" />
                    )}
                  </div>

                  {/* Client Info */}
                  <div className="flex-1">
                    <div className="flex items-center space-x-3 mb-2">
                      <h4 className="text-lg font-semibold text-white">{client.name}</h4>
                      <span
                        className={`px-2 py-0.5 text-xs rounded ${
                          client.protocol === 'usenet'
                            ? 'bg-blue-900/30 text-blue-400'
                            : 'bg-green-900/30 text-green-400'
                        }`}
                      >
                        {client.protocol.toUpperCase()}
                      </span>
                      <span className="px-2 py-0.5 bg-gray-800 text-gray-400 text-xs rounded">
                        Priority: {client.priority}
                      </span>
                    </div>

                    <div className="space-y-1 text-sm text-gray-400">
                      <p>
                        <span className="text-gray-500">Implementation:</span>{' '}
                        <span className="text-white">{client.implementation}</span>
                      </p>
                      <p>
                        <span className="text-gray-500">Host:</span>{' '}
                        <span className="text-white">
                          {client.host}:{client.port}
                        </span>
                      </p>
                      {client.category && (
                        <p>
                          <span className="text-gray-500">Category:</span>{' '}
                          <span className="text-white">{client.category}</span>
                        </p>
                      )}
                      {showAdvanced && client.recentPriority && (
                        <p>
                          <span className="text-gray-500">Recent Priority:</span>{' '}
                          <span className="text-white">{client.recentPriority}</span>
                        </p>
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

        {downloadClients.length === 0 && (
          <div className="text-center py-12">
            <ArrowDownTrayIcon className="w-16 h-16 text-gray-700 mx-auto mb-4" />
            <p className="text-gray-500 mb-2">No download clients configured</p>
            <p className="text-sm text-gray-400 mb-4">
              Add at least one download client to download combat sports events
            </p>
          </div>
        )}
      </div>

      {/* Completed Download Handling */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <h3 className="text-xl font-semibold text-white mb-4">Completed Download Handling</h3>

        <div className="space-y-4">
          <label className="flex items-start space-x-3 cursor-pointer">
            <input
              type="checkbox"
              checked={enableCompletedDownloadHandling}
              onChange={(e) => setEnableCompletedDownloadHandling(e.target.checked)}
              className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
            />
            <div>
              <span className="text-white font-medium">Enable</span>
              <p className="text-sm text-gray-400 mt-1">
                Automatically import completed downloads from download clients
              </p>
            </div>
          </label>

          {enableCompletedDownloadHandling && (
            <>
              <label className="flex items-start space-x-3 cursor-pointer">
                <input
                  type="checkbox"
                  checked={removeCompletedDownloads}
                  onChange={(e) => setRemoveCompletedDownloads(e.target.checked)}
                  className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                />
                <div>
                  <span className="text-white font-medium">Remove Completed Downloads</span>
                  <p className="text-sm text-gray-400 mt-1">
                    Remove completed downloads from download client history
                  </p>
                </div>
              </label>

              <div>
                <label className="block text-white font-medium mb-2">
                  Check For Finished Downloads Interval
                </label>
                <div className="flex items-center space-x-2">
                  <input
                    type="number"
                    value={checkForFinishedDownloads}
                    onChange={(e) => setCheckForFinishedDownloads(Number(e.target.value))}
                    className="w-32 px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    min="1"
                  />
                  <span className="text-gray-400">minute(s)</span>
                </div>
                <p className="text-sm text-gray-400 mt-1">
                  How often Fightarr will check download clients for completed downloads
                </p>
              </div>
            </>
          )}
        </div>
      </div>

      {/* Failed Download Handling */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <h3 className="text-xl font-semibold text-white mb-4">Failed Download Handling</h3>

        <div className="space-y-4">
          <label className="flex items-start space-x-3 cursor-pointer">
            <input
              type="checkbox"
              checked={enableFailedDownloadHandling}
              onChange={(e) => setEnableFailedDownloadHandling(e.target.checked)}
              className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
            />
            <div>
              <span className="text-white font-medium">Enable</span>
              <p className="text-sm text-gray-400 mt-1">
                Automatically handle failed downloads
              </p>
            </div>
          </label>

          {enableFailedDownloadHandling && (
            <>
              <label className="flex items-start space-x-3 cursor-pointer">
                <input
                  type="checkbox"
                  checked={redownloadFailedEvents}
                  onChange={(e) => setRedownloadFailedEvents(e.target.checked)}
                  className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                />
                <div>
                  <span className="text-white font-medium">Redownload</span>
                  <p className="text-sm text-gray-400 mt-1">
                    Automatically search for and attempt to download a different release
                  </p>
                </div>
              </label>

              <label className="flex items-start space-x-3 cursor-pointer">
                <input
                  type="checkbox"
                  checked={removeFailedDownloads}
                  onChange={(e) => setRemoveFailedDownloads(e.target.checked)}
                  className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                />
                <div>
                  <span className="text-white font-medium">Remove Failed Downloads</span>
                  <p className="text-sm text-gray-400 mt-1">
                    Remove failed downloads from download client history
                  </p>
                </div>
              </label>
            </>
          )}
        </div>
      </div>

      {/* Remote Path Mappings (Advanced) */}
      {showAdvanced && (
        <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-yellow-900/30 rounded-lg p-6">
          <div className="flex items-center justify-between mb-4">
            <div>
              <h3 className="text-xl font-semibold text-white">
                Remote Path Mappings
                <span className="ml-2 px-2 py-0.5 bg-yellow-900/30 text-yellow-400 text-xs rounded">
                  Advanced
                </span>
              </h3>
              <p className="text-sm text-gray-400 mt-1">
                Map download client paths to Fightarr paths (for Docker/remote clients)
              </p>
            </div>
            <button className="flex items-center px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors text-sm">
              <PlusIcon className="w-4 h-4 mr-2" />
              Add Mapping
            </button>
          </div>

          <div className="text-center py-8 text-gray-500">
            <p>No remote path mappings configured</p>
            <p className="text-sm mt-2">Only needed if download client is on a different system</p>
          </div>
        </div>
      )}

      {/* Save Button */}
      <div className="flex justify-end">
        <button className="px-6 py-3 bg-gradient-to-r from-red-600 to-red-700 hover:from-red-700 hover:to-red-800 text-white font-semibold rounded-lg shadow-lg transform transition hover:scale-105">
          Save Changes
        </button>
      </div>

      {/* Add Download Client Modal */}
      {showAddModal && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-3xl w-full max-h-[80vh] overflow-y-auto">
            <h3 className="text-2xl font-bold text-white mb-4">Add Download Client</h3>
            <p className="text-gray-400 mb-6">Select a download client type to configure</p>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-3 mb-6">
              {downloadClientTemplates.map((template, index) => (
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
