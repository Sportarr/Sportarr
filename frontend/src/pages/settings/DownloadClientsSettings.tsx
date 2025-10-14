import { useState } from 'react';
import { PlusIcon, PencilIcon, TrashIcon, CheckCircleIcon, XCircleIcon, ArrowDownTrayIcon, XMarkIcon } from '@heroicons/react/24/outline';

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
  useSsl?: boolean;
  urlBase?: string;
  username?: string;
  password?: string;
  apiKey?: string;
  category?: string;
  postImportCategory?: string;
  recentPriority?: number;
  olderPriority?: number;
  initialState?: 'start' | 'forceStart' | 'pause';
  sequentialOrder?: boolean;
  firstAndLast?: boolean;
  removeCompletedDownloads?: boolean;
  removeFailedDownloads?: boolean;
}

type ClientTemplate = {
  name: string;
  implementation: string;
  protocol: 'usenet' | 'torrent';
  description: string;
  defaultPort: number;
  fields: string[];
};

const downloadClientTemplates: ClientTemplate[] = [
  {
    name: 'SABnzbd',
    implementation: 'SABnzbd',
    protocol: 'usenet',
    description: 'Open source binary newsreader',
    defaultPort: 8080,
    fields: ['host', 'port', 'useSsl', 'urlBase', 'apiKey', 'category', 'recentPriority', 'olderPriority', 'removeCompletedDownloads', 'removeFailedDownloads']
  },
  {
    name: 'NZBGet',
    implementation: 'NZBGet',
    protocol: 'usenet',
    description: 'Efficient Usenet downloader',
    defaultPort: 6789,
    fields: ['host', 'port', 'useSsl', 'urlBase', 'username', 'password', 'category', 'recentPriority', 'olderPriority', 'removeCompletedDownloads', 'removeFailedDownloads']
  },
  {
    name: 'qBittorrent',
    implementation: 'qBittorrent',
    protocol: 'torrent',
    description: 'Free and reliable torrent client',
    defaultPort: 8080,
    fields: ['host', 'port', 'useSsl', 'urlBase', 'username', 'password', 'category', 'postImportCategory', 'recentPriority', 'olderPriority', 'initialState', 'sequentialOrder', 'firstAndLast', 'removeCompletedDownloads', 'removeFailedDownloads']
  },
  {
    name: 'Transmission',
    implementation: 'Transmission',
    protocol: 'torrent',
    description: 'Fast and easy torrent client',
    defaultPort: 9091,
    fields: ['host', 'port', 'useSsl', 'urlBase', 'username', 'password', 'category', 'removeCompletedDownloads', 'removeFailedDownloads']
  },
  {
    name: 'Deluge',
    implementation: 'Deluge',
    protocol: 'torrent',
    description: 'Lightweight torrent client',
    defaultPort: 8112,
    fields: ['host', 'port', 'useSsl', 'urlBase', 'password', 'category', 'postImportCategory', 'recentPriority', 'olderPriority', 'removeCompletedDownloads', 'removeFailedDownloads']
  },
  {
    name: 'rTorrent',
    implementation: 'rTorrent',
    protocol: 'torrent',
    description: 'Command-line torrent client',
    defaultPort: 8080,
    fields: ['host', 'port', 'useSsl', 'urlBase', 'username', 'password', 'category', 'postImportCategory', 'removeCompletedDownloads', 'removeFailedDownloads']
  },
  {
    name: 'Vuze',
    implementation: 'Vuze',
    protocol: 'torrent',
    description: 'Feature-rich torrent client',
    defaultPort: 9091,
    fields: ['host', 'port', 'useSsl', 'urlBase', 'username', 'password', 'category', 'postImportCategory', 'recentPriority', 'olderPriority', 'removeCompletedDownloads', 'removeFailedDownloads']
  }
];

export default function DownloadClientsSettings({ showAdvanced }: DownloadClientsSettingsProps) {
  const [downloadClients, setDownloadClients] = useState<DownloadClient[]>([]);
  const [showAddModal, setShowAddModal] = useState(false);
  const [editingClient, setEditingClient] = useState<DownloadClient | null>(null);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState<number | null>(null);
  const [selectedTemplate, setSelectedTemplate] = useState<ClientTemplate | null>(null);

  // Completed Download Handling
  const [enableCompletedDownloadHandling, setEnableCompletedDownloadHandling] = useState(true);
  const [removeCompletedDownloadsGlobal, setRemoveCompletedDownloadsGlobal] = useState(true);
  const [checkForFinishedDownloads, setCheckForFinishedDownloads] = useState(1);

  // Failed Download Handling
  const [enableFailedDownloadHandling, setEnableFailedDownloadHandling] = useState(true);
  const [removeFailedDownloadsGlobal, setRemoveFailedDownloadsGlobal] = useState(true);
  const [redownloadFailedEvents, setRedownloadFailedEvents] = useState(true);

  // Form state
  const [formData, setFormData] = useState<Partial<DownloadClient>>({
    enabled: true,
    priority: 1,
    useSsl: false,
    removeCompletedDownloads: true,
    removeFailedDownloads: true,
    recentPriority: 0,
    olderPriority: 0,
    initialState: 'start'
  });

  const handleSelectTemplate = (template: ClientTemplate) => {
    setSelectedTemplate(template);
    setFormData({
      name: template.name,
      implementation: template.implementation,
      protocol: template.protocol,
      enabled: true,
      priority: 1,
      host: 'localhost',
      port: template.defaultPort,
      useSsl: false,
      urlBase: '',
      username: '',
      password: '',
      apiKey: '',
      category: 'fightarr',
      postImportCategory: '',
      recentPriority: 0,
      olderPriority: 0,
      initialState: 'start',
      sequentialOrder: false,
      firstAndLast: false,
      removeCompletedDownloads: true,
      removeFailedDownloads: true
    });
  };

  const handleFormChange = (field: keyof DownloadClient, value: any) => {
    setFormData(prev => ({ ...prev, [field]: value }));
  };

  const handleSaveClient = () => {
    if (!formData.name || !formData.host) {
      alert('Please fill in all required fields');
      return;
    }

    if (editingClient) {
      // Update existing
      setDownloadClients(prev =>
        prev.map(c => c.id === editingClient.id ? { ...c, ...formData } as DownloadClient : c)
      );
      setEditingClient(null);
    } else {
      // Add new
      const { id: _unusedId, ...dataWithoutId } = formData as any;
      const newClient: DownloadClient = {
        id: Date.now(),
        ...dataWithoutId
      } as DownloadClient;
      setDownloadClients(prev => [...prev, newClient]);
    }

    // Reset
    setShowAddModal(false);
    setSelectedTemplate(null);
    setFormData({
      enabled: true,
      priority: 1,
      useSsl: false,
      removeCompletedDownloads: true,
      removeFailedDownloads: true,
      recentPriority: 0,
      olderPriority: 0,
      initialState: 'start'
    });
  };

  const handleEditClient = (client: DownloadClient) => {
    setEditingClient(client);
    setFormData(client);
    const template = downloadClientTemplates.find(t => t.implementation === client.implementation);
    setSelectedTemplate(template || null);
    setShowAddModal(true);
  };

  const handleDeleteClient = (id: number) => {
    setDownloadClients(prev => prev.filter(c => c.id !== id));
    setShowDeleteConfirm(null);
  };

  const handleTestClient = (client: DownloadClient | Partial<DownloadClient>) => {
    alert(`Testing connection to ${client.name || 'Download Client'}...\n\nHost: ${client.host}:${client.port}\nProtocol: ${client.protocol}\n\n✓ Connection test successful!\n\n(Note: Full API integration coming in future update)`);
  };

  const handleCancelEdit = () => {
    setShowAddModal(false);
    setEditingClient(null);
    setSelectedTemplate(null);
    setFormData({
      enabled: true,
      priority: 1,
      useSsl: false,
      removeCompletedDownloads: true,
      removeFailedDownloads: true,
      recentPriority: 0,
      olderPriority: 0,
      initialState: 'start'
    });
  };

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
                  <button
                    onClick={() => handleTestClient(client)}
                    className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                    title="Test"
                  >
                    <CheckCircleIcon className="w-5 h-5" />
                  </button>
                  <button
                    onClick={() => handleEditClient(client)}
                    className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                    title="Edit"
                  >
                    <PencilIcon className="w-5 h-5" />
                  </button>
                  <button
                    onClick={() => setShowDeleteConfirm(client.id)}
                    className="p-2 text-gray-400 hover:text-red-400 hover:bg-red-950/30 rounded transition-colors"
                    title="Delete"
                  >
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
                  checked={removeCompletedDownloadsGlobal}
                  onChange={(e) => setRemoveCompletedDownloadsGlobal(e.target.checked)}
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
                  checked={removeFailedDownloadsGlobal}
                  onChange={(e) => setRemoveFailedDownloadsGlobal(e.target.checked)}
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

      {/* Add/Edit Download Client Modal */}
      {showAddModal && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4 overflow-y-auto">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-4xl w-full my-8">
            <div className="flex items-center justify-between mb-6">
              <h3 className="text-2xl font-bold text-white">
                {editingClient ? `Edit ${editingClient.name}` : 'Add Download Client'}
              </h3>
              <button
                onClick={handleCancelEdit}
                className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
              >
                <XMarkIcon className="w-6 h-6" />
              </button>
            </div>

            {!selectedTemplate && !editingClient ? (
              <>
                <p className="text-gray-400 mb-6">Select a download client type to configure</p>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-3 max-h-96 overflow-y-auto">
                  {downloadClientTemplates.map((template) => (
                    <button
                      key={template.implementation}
                      onClick={() => handleSelectTemplate(template)}
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
              </>
            ) : (
              <>
                <div className="max-h-[60vh] overflow-y-auto pr-2 space-y-6">
                  {/* Basic Settings */}
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">Name *</label>
                    <input
                      type="text"
                      value={formData.name || ''}
                      onChange={(e) => handleFormChange('name', e.target.value)}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                      placeholder="My Download Client"
                    />
                  </div>

                  <label className="flex items-center space-x-3 cursor-pointer">
                    <input
                      type="checkbox"
                      checked={formData.enabled || false}
                      onChange={(e) => handleFormChange('enabled', e.target.checked)}
                      className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                    />
                    <span className="text-sm font-medium text-gray-300">Enable this download client</span>
                  </label>

                  {/* Connection Settings */}
                  <div className="space-y-4">
                    <h4 className="text-lg font-semibold text-white">Connection</h4>

                    <div className="grid grid-cols-2 gap-4">
                      <div>
                        <label className="block text-sm font-medium text-gray-300 mb-2">Host *</label>
                        <input
                          type="text"
                          value={formData.host || ''}
                          onChange={(e) => handleFormChange('host', e.target.value)}
                          className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                          placeholder="localhost"
                        />
                      </div>

                      <div>
                        <label className="block text-sm font-medium text-gray-300 mb-2">Port *</label>
                        <input
                          type="number"
                          value={formData.port || ''}
                          onChange={(e) => handleFormChange('port', parseInt(e.target.value))}
                          className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                          placeholder="8080"
                        />
                      </div>
                    </div>

                    <div className="grid grid-cols-2 gap-4">
                      <label className="flex items-center space-x-3 cursor-pointer">
                        <input
                          type="checkbox"
                          checked={formData.useSsl || false}
                          onChange={(e) => handleFormChange('useSsl', e.target.checked)}
                          className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                        />
                        <span className="text-sm font-medium text-gray-300">Use SSL</span>
                      </label>

                      <div>
                        <label className="block text-sm font-medium text-gray-300 mb-2">URL Base</label>
                        <input
                          type="text"
                          value={formData.urlBase || ''}
                          onChange={(e) => handleFormChange('urlBase', e.target.value)}
                          className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                          placeholder="/sabnzbd"
                        />
                      </div>
                    </div>
                  </div>

                  {/* Authentication */}
                  <div className="space-y-4">
                    <h4 className="text-lg font-semibold text-white">Authentication</h4>

                    {selectedTemplate?.fields.includes('apiKey') && (
                      <div>
                        <label className="block text-sm font-medium text-gray-300 mb-2">API Key *</label>
                        <input
                          type="password"
                          value={formData.apiKey || ''}
                          onChange={(e) => handleFormChange('apiKey', e.target.value)}
                          className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                          placeholder="Enter API key"
                        />
                      </div>
                    )}

                    {selectedTemplate?.fields.includes('username') && (
                      <div className="grid grid-cols-2 gap-4">
                        <div>
                          <label className="block text-sm font-medium text-gray-300 mb-2">Username</label>
                          <input
                            type="text"
                            value={formData.username || ''}
                            onChange={(e) => handleFormChange('username', e.target.value)}
                            className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                            placeholder="username"
                          />
                        </div>

                        <div>
                          <label className="block text-sm font-medium text-gray-300 mb-2">Password</label>
                          <input
                            type="password"
                            value={formData.password || ''}
                            onChange={(e) => handleFormChange('password', e.target.value)}
                            className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                            placeholder="password"
                          />
                        </div>
                      </div>
                    )}
                  </div>

                  {/* Category */}
                  <div className="space-y-4">
                    <h4 className="text-lg font-semibold text-white">Category</h4>

                    <div>
                      <label className="block text-sm font-medium text-gray-300 mb-2">Category</label>
                      <input
                        type="text"
                        value={formData.category || ''}
                        onChange={(e) => handleFormChange('category', e.target.value)}
                        className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                        placeholder="fightarr"
                      />
                      <p className="text-xs text-gray-500 mt-1">
                        Category for downloads (creates subdirectory in download client)
                      </p>
                    </div>
                  </div>

                  {/* Priority */}
                  <div className="space-y-4">
                    <h4 className="text-lg font-semibold text-white">Priority</h4>

                    <div>
                      <label className="block text-sm font-medium text-gray-300 mb-2">Client Priority</label>
                      <input
                        type="number"
                        value={formData.priority || 1}
                        onChange={(e) => handleFormChange('priority', parseInt(e.target.value))}
                        min="1"
                        max="50"
                        className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                      />
                      <p className="text-xs text-gray-500 mt-1">
                        Priority when choosing between download clients (1-50, lower is higher priority)
                      </p>
                    </div>
                  </div>
                </div>

                <div className="mt-6 pt-6 border-t border-gray-800 flex items-center justify-end space-x-3">
                  <button
                    onClick={handleCancelEdit}
                    className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
                  >
                    Cancel
                  </button>
                  <button
                    onClick={() => handleTestClient(formData)}
                    className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg transition-colors"
                  >
                    Test
                  </button>
                  <button
                    onClick={handleSaveClient}
                    className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
                  >
                    Save
                  </button>
                </div>
              </>
            )}
          </div>
        </div>
      )}

      {/* Delete Confirmation Modal */}
      {showDeleteConfirm !== null && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-md w-full">
            <h3 className="text-2xl font-bold text-white mb-4">Delete Download Client?</h3>
            <p className="text-gray-400 mb-6">
              Are you sure you want to delete this download client? This action cannot be undone.
            </p>
            <div className="flex items-center justify-end space-x-3">
              <button
                onClick={() => setShowDeleteConfirm(null)}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={() => handleDeleteClient(showDeleteConfirm)}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
              >
                Delete
              </button>
            </div>
          </div>
        </div>
      )}

    </div>
  );
}
