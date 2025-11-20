import { useState, useEffect, useRef } from 'react';
import { PlusIcon, PencilIcon, TrashIcon, CheckCircleIcon, XCircleIcon, ArrowDownTrayIcon, XMarkIcon } from '@heroicons/react/24/outline';
import { toast } from 'sonner';
import apiClient from '../../api/client';
import { apiGet, apiPut } from '../../utils/api';
import SettingsHeader from '../../components/SettingsHeader';
import { useUnsavedChanges } from '../../hooks/useUnsavedChanges';

interface DownloadClientsSettingsProps {
  showAdvanced: boolean;
}

interface DownloadClient {
  id: number;
  name: string;
  type: number; // Backend uses enum: QBittorrent=0, Transmission=1, Deluge=2, RTorrent=3, UTorrent=4, Sabnzbd=5, NzbGet=6
  host: string;
  port: number;
  username?: string;
  password?: string;
  apiKey?: string;
  urlBase?: string; // URL base path (e.g., "/sabnzbd" for SABnzbd, "" for root)
  category: string;
  useSsl: boolean;
  disableSslCertificateValidation?: boolean;
  enabled: boolean;
  priority: number;
  created?: string;
  lastModified?: string;
}

interface RemotePathMapping {
  id: number;
  host: string;
  remotePath: string;
  localPath: string;
}

// Map frontend template names to backend type enums
const clientTypeMap: Record<string, number> = {
  'qBittorrent': 0,
  'Transmission': 1,
  'Deluge': 2,
  'rTorrent': 3,
  'uTorrent': 4,
  'SABnzbd': 5,
  'NZBGet': 6
};

const clientTypeNameMap: Record<number, string> = {
  0: 'qBittorrent',
  1: 'Transmission',
  2: 'Deluge',
  3: 'rTorrent',
  4: 'uTorrent',
  5: 'SABnzbd',
  6: 'NZBGet'
};

// Determine protocol based on type
const getProtocol = (type: number): 'usenet' | 'torrent' => {
  const protocol = (type === 5 || type === 6) ? 'usenet' : 'torrent';
  console.log(`[DEBUG] getProtocol: type=${type}, protocol=${protocol}, type===5: ${type === 5}, type===6: ${type === 6}`);
  return protocol;
};

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
  const [isLoading, setIsLoading] = useState(false);
  const [testResult, setTestResult] = useState<{ success: boolean; message: string } | null>(null);

  // Remote Path Mappings state
  const [pathMappings, setPathMappings] = useState<RemotePathMapping[]>([]);
  const [showPathMappingModal, setShowPathMappingModal] = useState(false);
  const [editingPathMapping, setEditingPathMapping] = useState<RemotePathMapping | null>(null);
  const [showDeletePathMappingConfirm, setShowDeletePathMappingConfirm] = useState<number | null>(null);
  const [pathMappingForm, setPathMappingForm] = useState({ host: '', remotePath: '', localPath: '' });

  // Load download clients and settings on mount
  useEffect(() => {
    loadDownloadClients();
    loadSettings();
    loadPathMappings();
  }, []);

  const loadDownloadClients = async () => {
    try {
      setIsLoading(true);
      const response = await apiClient.get('/downloadclient');
      console.log('[DEBUG] Loaded download clients from API:', response.data);
      response.data.forEach((client: any) => {
        console.log(`[DEBUG] Client: ${client.name}, Type: ${client.type}, Protocol: ${getProtocol(client.type)}, UrlBase: ${client.urlBase}`);
      });
      setDownloadClients(response.data);
    } catch (error) {
      console.error('Failed to load download clients:', error);
    } finally {
      setIsLoading(false);
    }
  };

  const loadSettings = async () => {
    try {
      const response = await apiGet('/api/settings');
      if (response.ok) {
        const data = await response.json();

        const loadedSettings = {
          enableCompletedDownloadHandling: data.enableCompletedDownloadHandling ?? true,
          removeCompletedDownloadsGlobal: data.removeCompletedDownloads ?? true,
          checkForFinishedDownloads: data.checkForFinishedDownloadInterval ?? 1,
          enableFailedDownloadHandling: data.enableFailedDownloadHandling ?? true,
          redownloadFailedEvents: data.redownloadFailedDownloads ?? true,
          removeFailedDownloadsGlobal: data.removeFailedDownloads ?? true
        };

        setEnableCompletedDownloadHandling(loadedSettings.enableCompletedDownloadHandling);
        setRemoveCompletedDownloadsGlobal(loadedSettings.removeCompletedDownloadsGlobal);
        setCheckForFinishedDownloads(loadedSettings.checkForFinishedDownloads);
        setEnableFailedDownloadHandling(loadedSettings.enableFailedDownloadHandling);
        setRedownloadFailedEvents(loadedSettings.redownloadFailedEvents);
        setRemoveFailedDownloadsGlobal(loadedSettings.removeFailedDownloadsGlobal);

        initialSettings.current = loadedSettings;
        setHasUnsavedChanges(false);
      }
    } catch (error) {
      console.error('Failed to load settings:', error);
    }
  };

  const handleSaveSettings = async () => {
    setSaving(true);
    try {
      // First fetch current settings
      const response = await apiGet('/api/settings');
      if (!response.ok) throw new Error('Failed to fetch current settings');

      const currentSettings = await response.json();

      // Update with new values
      const updatedSettings = {
        ...currentSettings,
        enableCompletedDownloadHandling,
        removeCompletedDownloads: removeCompletedDownloadsGlobal,
        checkForFinishedDownloadInterval: checkForFinishedDownloads,
        enableFailedDownloadHandling,
        redownloadFailedDownloads: redownloadFailedEvents,
        removeFailedDownloads: removeFailedDownloadsGlobal,
      };

      // Save to API
      await apiPut('/api/settings', updatedSettings);

      // Update initial settings and reset unsaved changes flag
      initialSettings.current = {
        enableCompletedDownloadHandling,
        removeCompletedDownloadsGlobal,
        checkForFinishedDownloads,
        enableFailedDownloadHandling,
        removeFailedDownloadsGlobal,
        redownloadFailedEvents
      };
      setHasUnsavedChanges(false);
    } catch (error) {
      console.error('Failed to save settings:', error);
      toast.error('Save Failed', {
        description: 'Failed to save settings. Please try again.',
      });
    } finally {
      setSaving(false);
    }
  };

  // Completed Download Handling
  const [enableCompletedDownloadHandling, setEnableCompletedDownloadHandling] = useState(true);
  const [removeCompletedDownloadsGlobal, setRemoveCompletedDownloadsGlobal] = useState(true);
  const [checkForFinishedDownloads, setCheckForFinishedDownloads] = useState(1);

  // Failed Download Handling
  const [enableFailedDownloadHandling, setEnableFailedDownloadHandling] = useState(true);
  const [removeFailedDownloadsGlobal, setRemoveFailedDownloadsGlobal] = useState(true);
  const [redownloadFailedEvents, setRedownloadFailedEvents] = useState(true);

  // Save state
  const [saving, setSaving] = useState(false);
  const [hasUnsavedChanges, setHasUnsavedChanges] = useState(false);
  const initialSettings = useRef<{
    enableCompletedDownloadHandling: boolean;
    removeCompletedDownloadsGlobal: boolean;
    checkForFinishedDownloads: number;
    enableFailedDownloadHandling: boolean;
    removeFailedDownloadsGlobal: boolean;
    redownloadFailedEvents: boolean;
  } | null>(null);
  const { blockNavigation } = useUnsavedChanges(hasUnsavedChanges);

  // Detect changes
  useEffect(() => {
    if (!initialSettings.current) return;
    const currentSettings = {
      enableCompletedDownloadHandling,
      removeCompletedDownloadsGlobal,
      checkForFinishedDownloads,
      enableFailedDownloadHandling,
      removeFailedDownloadsGlobal,
      redownloadFailedEvents
    };
    const hasChanges = JSON.stringify(currentSettings) !== JSON.stringify(initialSettings.current);
    setHasUnsavedChanges(hasChanges);
  }, [enableCompletedDownloadHandling, removeCompletedDownloadsGlobal, checkForFinishedDownloads,
      enableFailedDownloadHandling, removeFailedDownloadsGlobal, redownloadFailedEvents]);

  // Note: In-app navigation blocking would require React Router's unstable_useBlocker
  // For now, we only block browser refresh/close via the useUnsavedChanges hook

  // Form state
  const [formData, setFormData] = useState<Partial<DownloadClient>>({
    enabled: true,
    priority: 1,
    useSsl: false,
    disableSslCertificateValidation: false,
    category: 'sportarr',
    type: 0,
    name: '',
    host: 'localhost',
    port: 8080
  });

  const handleSelectTemplate = (template: ClientTemplate) => {
    setSelectedTemplate(template);
    setTestResult(null);
    setFormData({
      name: template.name,
      type: clientTypeMap[template.implementation] ?? 0,
      enabled: true,
      priority: 1,
      host: 'localhost',
      port: template.defaultPort,
      useSsl: false,
      disableSslCertificateValidation: false,
      username: '',
      password: '',
      apiKey: '',
      category: 'sportarr'
    });
  };

  const handleFormChange = (field: keyof DownloadClient, value: any) => {
    setFormData(prev => ({ ...prev, [field]: value }));
  };

  const handleSaveClient = async () => {
    if (!formData.name || !formData.host) {
      return;
    }

    try {
      setIsLoading(true);
      console.log('[DEBUG] Saving download client with data:', formData);
      console.log('[DEBUG] UrlBase value being saved:', formData.urlBase);

      if (editingClient) {
        // Update existing
        await apiClient.put(`/downloadclient/${editingClient.id}`, formData);
      } else {
        // Add new
        await apiClient.post('/downloadclient', formData);
      }

      // Reload clients from database
      await loadDownloadClients();

      // Reset
      setShowAddModal(false);
      setEditingClient(null);
      setSelectedTemplate(null);
      setTestResult(null);
      setFormData({
        enabled: true,
        priority: 1,
        useSsl: false,
        category: 'sportarr',
        type: 0,
        name: '',
        host: 'localhost',
        port: 8080
      });
    } catch (error) {
      console.error('Failed to save download client:', error);
      toast.error('Save Failed', {
        description: 'Failed to save download client. Please check the console for details.',
      });
    } finally {
      setIsLoading(false);
    }
  };

  const handleEditClient = (client: DownloadClient) => {
    console.log('[DEBUG] Editing client:', client);
    console.log('[DEBUG] Client urlBase:', client.urlBase);
    setEditingClient(client);
    setFormData(client);
    console.log('[DEBUG] FormData after setFormData:', client);
    setTestResult(null);
    const clientName = clientTypeNameMap[client.type];
    const template = downloadClientTemplates.find(t => t.implementation === clientName);
    setSelectedTemplate(template || null);
    setShowAddModal(true);
  };

  const handleDeleteClient = async (id: number) => {
    try {
      setIsLoading(true);
      await apiClient.delete(`/downloadclient/${id}`);
      await loadDownloadClients();
      setShowDeleteConfirm(null);
    } catch (error) {
      console.error('Failed to delete download client:', error);
      toast.error('Delete Failed', {
        description: 'Failed to delete download client. Please try again.',
      });
    } finally {
      setIsLoading(false);
    }
  };

  const handleTestClient = async (client: DownloadClient | Partial<DownloadClient>) => {
    try {
      setIsLoading(true);
      setTestResult(null);
      const response = await apiClient.post('/downloadclient/test', client);
      setTestResult({ success: response.data.success, message: response.data.message || 'Connection successful!' });
    } catch (error: any) {
      console.error('Test failed:', error);
      setTestResult({ success: false, message: error.response?.data?.message || 'Connection test failed!' });
    } finally {
      setIsLoading(false);
    }
  };

  const handleCancelEdit = () => {
    setShowAddModal(false);
    setEditingClient(null);
    setSelectedTemplate(null);
    setTestResult(null);
    setFormData({
      enabled: true,
      priority: 1,
      useSsl: false,
      category: 'sportarr',
      type: 0,
      name: '',
      host: 'localhost',
      port: 8080
    });
  };

  // Remote Path Mapping Functions
  const loadPathMappings = async () => {
    try {
      const response = await apiClient.get('/remotepathmapping');
      setPathMappings(response.data);
    } catch (error) {
      console.error('Failed to load path mappings:', error);
    }
  };

  const handleAddPathMapping = () => {
    setEditingPathMapping(null);
    setPathMappingForm({ host: '', remotePath: '', localPath: '' });
    setShowPathMappingModal(true);
  };

  const handleEditPathMapping = (mapping: RemotePathMapping) => {
    setEditingPathMapping(mapping);
    setPathMappingForm({
      host: mapping.host,
      remotePath: mapping.remotePath,
      localPath: mapping.localPath
    });
    setShowPathMappingModal(true);
  };

  const handleSavePathMapping = async () => {
    try {
      setIsLoading(true);
      if (editingPathMapping) {
        // Update existing
        await apiClient.put(`/remotepathmapping/${editingPathMapping.id}`, pathMappingForm);
      } else {
        // Create new
        await apiClient.post('/remotepathmapping', pathMappingForm);
      }
      await loadPathMappings();
      setShowPathMappingModal(false);
      setPathMappingForm({ host: '', remotePath: '', localPath: '' });
    } catch (error) {
      console.error('Failed to save path mapping:', error);
      toast.error('Save Failed', {
        description: 'Failed to save path mapping. Please try again.',
      });
    } finally {
      setIsLoading(false);
    }
  };

  const handleDeletePathMapping = async (id: number) => {
    try {
      await apiClient.delete(`/remotepathmapping/${id}`);
      await loadPathMappings();
      setShowDeletePathMappingConfirm(null);
    } catch (error) {
      console.error('Failed to delete path mapping:', error);
      toast.error('Delete Failed', {
        description: 'Failed to delete path mapping. Please try again.',
      });
    }
  };

  return (
    <div>
      <SettingsHeader
        title="Download Clients"
        subtitle="Configure download clients for Usenet and torrent downloads"
        onSave={handleSaveSettings}
        isSaving={saving}
        hasUnsavedChanges={hasUnsavedChanges}
        saveButtonText="Save Changes"
      />

      <div className="max-w-6xl mx-auto px-6">

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
                          getProtocol(client.type) === 'usenet'
                            ? 'bg-blue-900/30 text-blue-400'
                            : 'bg-green-900/30 text-green-400'
                        }`}
                      >
                        {getProtocol(client.type).toUpperCase()}
                      </span>
                      <span className="px-2 py-0.5 bg-gray-800 text-gray-400 text-xs rounded">
                        Priority: {client.priority}
                      </span>
                    </div>

                    <div className="space-y-1 text-sm text-gray-400">
                      <p>
                        <span className="text-gray-500">Implementation:</span>{' '}
                        <span className="text-white">{clientTypeNameMap[client.type]}</span>
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
                  How often Sportarr will check download clients for completed downloads
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

      {/* Remote Path Mappings */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <div className="flex items-center justify-between mb-4">
          <div>
            <h3 className="text-xl font-semibold text-white">Remote Path Mappings</h3>
            <p className="text-sm text-gray-400 mt-1">
              Map download client paths to Sportarr paths (required for Docker/remote clients)
            </p>
          </div>
            <button
              onClick={handleAddPathMapping}
              className="flex items-center px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors text-sm"
            >
              <PlusIcon className="w-4 h-4 mr-2" />
              Add Mapping
            </button>
          </div>

          {pathMappings.length > 0 ? (
            <div className="space-y-3">
              {pathMappings.map((mapping) => (
                <div
                  key={mapping.id}
                  className="bg-gray-900/50 border border-gray-800 rounded-lg p-4 hover:border-gray-700 transition-colors"
                >
                  <div className="flex items-start justify-between">
                    <div className="flex-1 space-y-2">
                      <div className="grid grid-cols-3 gap-4 text-sm">
                        <div>
                          <span className="text-gray-500">Host:</span>
                          <p className="text-white mt-1">{mapping.host}</p>
                        </div>
                        <div>
                          <span className="text-gray-500">Remote Path:</span>
                          <p className="text-white mt-1 font-mono text-xs break-all">
                            {mapping.remotePath}
                          </p>
                        </div>
                        <div>
                          <span className="text-gray-500">Local Path:</span>
                          <p className="text-white mt-1 font-mono text-xs break-all">
                            {mapping.localPath}
                          </p>
                        </div>
                      </div>
                    </div>

                    {/* Actions */}
                    <div className="flex items-center space-x-2 ml-4">
                      <button
                        onClick={() => handleEditPathMapping(mapping)}
                        className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                        title="Edit"
                      >
                        <PencilIcon className="w-5 h-5" />
                      </button>
                      <button
                        onClick={() => setShowDeletePathMappingConfirm(mapping.id)}
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
          ) : (
            <div className="text-center py-8 text-gray-500">
              <p>No remote path mappings configured</p>
              <p className="text-sm mt-2">Only needed if download client is on a different system or in Docker</p>
            </div>
          )}
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

                    <label className="flex items-center space-x-3 cursor-pointer">
                      <input
                        type="checkbox"
                        checked={formData.useSsl || false}
                        onChange={(e) => handleFormChange('useSsl', e.target.checked)}
                        className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                      />
                      <span className="text-sm font-medium text-gray-300">Use SSL</span>
                    </label>

                    {formData.useSsl && (
                      <label className="flex items-center space-x-3 cursor-pointer">
                        <input
                          type="checkbox"
                          checked={formData.disableSslCertificateValidation || false}
                          onChange={(e) => handleFormChange('disableSslCertificateValidation', e.target.checked)}
                          className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                        />
                        <div>
                          <span className="text-sm font-medium text-gray-300">Disable SSL Certificate Validation</span>
                          <p className="text-xs text-gray-500 mt-1">
                            Allows connections with self-signed certificates. Only enable this for local networks.
                          </p>
                        </div>
                      </label>
                    )}

                    {selectedTemplate?.fields.includes('urlBase') && (
                      <div>
                        <label className="block text-sm font-medium text-gray-300 mb-2">URL Base</label>
                        <input
                          type="text"
                          value={formData.urlBase || ''}
                          onChange={(e) => handleFormChange('urlBase', e.target.value)}
                          className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                          placeholder={
                            selectedTemplate.name === 'SABnzbd' ? 'Leave empty for default (root)' :
                            selectedTemplate.name === 'NZBGet' ? 'Leave empty for default (root)' :
                            selectedTemplate.name === 'Transmission' ? '/transmission' :
                            selectedTemplate.name === 'Deluge' ? 'Leave empty for default (root)' :
                            selectedTemplate.name === 'rTorrent' ? '/rutorrent' :
                            selectedTemplate.name === 'qBittorrent' ? 'Leave empty for default (root)' :
                            ''
                          }
                        />
                        <p className="text-xs text-gray-500 mt-1">
                          {selectedTemplate.name === 'SABnzbd' && 'URL base configured in SABnzbd. Default is root (leave empty). Use /sabnzbd only if configured in SABnzbd settings.'}
                          {selectedTemplate.name === 'NZBGet' && 'URL base for NZBGet web interface. Default is root (leave empty). Use /nzbget only if configured in NZBGet settings.'}
                          {selectedTemplate.name === 'qBittorrent' && 'URL path prefix for qBittorrent Web UI. Default is root (leave empty). Set only if configured in qBittorrent settings.'}
                          {selectedTemplate.name === 'Transmission' && 'RPC URL path for Transmission. Default is /transmission. Leave empty only if you changed it in Transmission settings.'}
                          {selectedTemplate.name === 'Deluge' && 'Base URL for Deluge web interface. Default is root (leave empty). Use /deluge only if configured in Deluge settings.'}
                          {selectedTemplate.name === 'rTorrent' && 'URL base for ruTorrent web interface. Default is /rutorrent. Leave empty only if you changed it in ruTorrent settings.'}
                          {selectedTemplate.name === 'Vuze' && 'URL base for Vuze web interface. Default is root (leave empty).'}
                        </p>
                      </div>
                    )}
                  </div>

                  {/* Authentication */}
                  <div className="space-y-4">
                    <h4 className="text-lg font-semibold text-white">Authentication</h4>

                    {selectedTemplate?.fields.includes('apiKey') && (
                      <div>
                        <label className="block text-sm font-medium text-gray-300 mb-2">
                          API Key {editingClient ? '' : '*'}
                        </label>
                        <input
                          type="password"
                          value={formData.apiKey || ''}
                          onChange={(e) => handleFormChange('apiKey', e.target.value)}
                          className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                          placeholder={editingClient ? "Leave blank to keep existing API key" : "Enter API key"}
                        />
                        {editingClient && (
                          <p className="text-xs text-gray-500 mt-1">
                            Leave blank to keep the existing API key, or enter a new one to update it
                          </p>
                        )}
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
                            placeholder={editingClient ? "Leave blank to keep existing" : "password"}
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
                        placeholder="sportarr"
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

                {/* Test Result Display */}
                {testResult && (
                  <div className={`mt-4 p-4 rounded-lg border ${
                    testResult.success
                      ? 'bg-green-950/30 border-green-900/50'
                      : 'bg-red-950/30 border-red-900/50'
                  }`}>
                    <div className="flex items-center space-x-2">
                      {testResult.success ? (
                        <CheckCircleIcon className="w-5 h-5 text-green-400" />
                      ) : (
                        <XCircleIcon className="w-5 h-5 text-red-400" />
                      )}
                      <span className={testResult.success ? 'text-green-300' : 'text-red-300'}>
                        {testResult.message}
                      </span>
                    </div>
                  </div>
                )}

                <div className="mt-6 pt-6 border-t border-gray-800 flex items-center justify-end space-x-3">
                  <button
                    onClick={handleCancelEdit}
                    className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
                    disabled={isLoading}
                  >
                    Cancel
                  </button>
                  <button
                    onClick={() => handleTestClient(formData)}
                    className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                    disabled={isLoading}
                  >
                    {isLoading ? 'Testing...' : 'Test'}
                  </button>
                  <button
                    onClick={handleSaveClient}
                    className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                    disabled={isLoading}
                  >
                    {isLoading ? 'Saving...' : 'Save'}
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

      {/* Add/Edit Remote Path Mapping Modal */}
      {showPathMappingModal && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-2xl w-full">
            <h3 className="text-2xl font-bold text-white mb-6">
              {editingPathMapping ? 'Edit Remote Path Mapping' : 'Add Remote Path Mapping'}
            </h3>

            <div className="space-y-4 mb-6">
              {/* Info Box */}
              <div className="bg-blue-950/30 border border-blue-900/50 rounded-lg p-4">
                <p className="text-sm text-blue-300">
                  <strong>When to use:</strong> If your download client is on a different machine or in Docker,
                  the paths it reports may not match where Sportarr can access them. This mapping translates
                  the download client's path to the path Sportarr should use.
                </p>
                <p className="text-sm text-blue-300 mt-2">
                  <strong>Example:</strong> Download client reports <code className="bg-blue-900/30 px-1 rounded">/downloads/complete/</code>
                  but Sportarr accesses it at <code className="bg-blue-900/30 px-1 rounded">\\192.168.1.100\downloads\complete\</code>
                </p>
              </div>

              {/* Host */}
              <div>
                <label className="block text-white font-medium mb-2">
                  Host
                  <span className="text-red-500 ml-1">*</span>
                </label>
                <input
                  type="text"
                  value={pathMappingForm.host}
                  onChange={(e) => setPathMappingForm({ ...pathMappingForm, host: e.target.value })}
                  placeholder="localhost, 192.168.1.100, or download-client-hostname"
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                />
                <p className="text-sm text-gray-400 mt-1">
                  Download client host name or IP address (must match the download client's configured host)
                </p>
              </div>

              {/* Remote Path */}
              <div>
                <label className="block text-white font-medium mb-2">
                  Remote Path
                  <span className="text-red-500 ml-1">*</span>
                </label>
                <input
                  type="text"
                  value={pathMappingForm.remotePath}
                  onChange={(e) => setPathMappingForm({ ...pathMappingForm, remotePath: e.target.value })}
                  placeholder="/downloads/complete/sportarr/ or C:\Downloads\Complete\Sportarr\"
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600 font-mono text-sm"
                />
                <p className="text-sm text-gray-400 mt-1">
                  Path as reported by the download client (use forward slashes for Linux/Docker paths)
                </p>
              </div>

              {/* Local Path */}
              <div>
                <label className="block text-white font-medium mb-2">
                  Local Path
                  <span className="text-red-500 ml-1">*</span>
                </label>
                <input
                  type="text"
                  value={pathMappingForm.localPath}
                  onChange={(e) => setPathMappingForm({ ...pathMappingForm, localPath: e.target.value })}
                  placeholder="\\192.168.1.100\downloads\complete\sportarr\ or /mnt/downloads/complete/sportarr/"
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600 font-mono text-sm"
                />
                <p className="text-sm text-gray-400 mt-1">
                  Path that Sportarr should use to access the same location
                </p>
              </div>
            </div>

            <div className="flex items-center justify-end space-x-3">
              <button
                onClick={() => setShowPathMappingModal(false)}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={handleSavePathMapping}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                disabled={!pathMappingForm.host || !pathMappingForm.remotePath || !pathMappingForm.localPath}
              >
                Save
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Delete Path Mapping Confirmation Modal */}
      {showDeletePathMappingConfirm !== null && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-md w-full">
            <h3 className="text-2xl font-bold text-white mb-4">Delete Path Mapping?</h3>
            <p className="text-gray-400 mb-6">
              Are you sure you want to delete this remote path mapping? This action cannot be undone.
            </p>
            <div className="flex items-center justify-end space-x-3">
              <button
                onClick={() => setShowDeletePathMappingConfirm(null)}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={() => handleDeletePathMapping(showDeletePathMappingConfirm)}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
              >
                Delete
              </button>
            </div>
          </div>
        </div>
      )}

      </div>
    </div>
  );
}
