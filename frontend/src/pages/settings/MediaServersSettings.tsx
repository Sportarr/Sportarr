import { useState, useEffect } from 'react';
import { PlusIcon, PencilIcon, TrashIcon, ServerIcon, XMarkIcon, CheckCircleIcon, ArrowPathIcon, ExclamationTriangleIcon, ChevronDownIcon } from '@heroicons/react/24/outline';
import { apiGet, apiPost, apiPut, apiDelete } from '../../utils/api';

interface MediaServersSettingsProps {
  showAdvanced?: boolean;
}

interface MediaServerConnection {
  id: number;
  name: string;
  type: string;
  url: string;
  apiKey: string;
  librarySectionId?: string;
  librarySectionName?: string;
  pathMapFrom?: string;
  pathMapTo?: string;
  updateLibrary: boolean;
  usePartialScan: boolean;
  enabled: boolean;
  lastTested?: string;
  isHealthy: boolean;
  lastError?: string;
  serverName?: string;
  serverVersion?: string;
  created: string;
  modified: string;
}

interface MediaServerLibrary {
  id: string;
  name: string;
  type: string;
  path?: string;
}

interface TestResult {
  success: boolean;
  message: string;
  serverName?: string;
  serverVersion?: string;
  libraries: MediaServerLibrary[];
}

type ServerType = 'Plex' | 'Jellyfin' | 'Emby';

const serverTypes: { type: ServerType; name: string; description: string; icon: string; defaultPort: number }[] = [
  {
    type: 'Plex',
    name: 'Plex',
    description: 'Connect to Plex Media Server for library refresh notifications',
    icon: '🎬',
    defaultPort: 32400
  },
  {
    type: 'Jellyfin',
    name: 'Jellyfin',
    description: 'Connect to Jellyfin server for library refresh notifications',
    icon: '🎞️',
    defaultPort: 8096
  },
  {
    type: 'Emby',
    name: 'Emby',
    description: 'Connect to Emby server for library refresh notifications',
    icon: '📺',
    defaultPort: 8096
  }
];

export default function MediaServersSettings({ showAdvanced = false }: MediaServersSettingsProps) {
  const [connections, setConnections] = useState<MediaServerConnection[]>([]);
  const [loading, setLoading] = useState(true);
  const [showAddModal, setShowAddModal] = useState(false);
  const [editingConnection, setEditingConnection] = useState<MediaServerConnection | null>(null);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState<number | null>(null);
  const [selectedType, setSelectedType] = useState<ServerType | null>(null);

  // Form state
  const [formData, setFormData] = useState<Partial<MediaServerConnection>>({
    enabled: true,
    updateLibrary: true,
    usePartialScan: true
  });

  // Libraries state for dropdown
  const [libraries, setLibraries] = useState<MediaServerLibrary[]>([]);
  const [loadingLibraries, setLoadingLibraries] = useState(false);

  // Test state
  const [testing, setTesting] = useState(false);
  const [testResult, setTestResult] = useState<TestResult | null>(null);

  // Saving state
  const [saving, setSaving] = useState(false);

  // Refresh state
  const [refreshing, setRefreshing] = useState<number | null>(null);

  useEffect(() => {
    fetchConnections();
  }, []);

  const fetchConnections = async () => {
    try {
      const response = await apiGet('/api/mediaserver');
      if (response.ok) {
        const data = await response.json();
        setConnections(data);
      }
    } catch (error) {
      console.error('Failed to fetch media server connections:', error);
    } finally {
      setLoading(false);
    }
  };

  const handleAddClick = () => {
    setSelectedType(null);
    setFormData({
      enabled: true,
      updateLibrary: true,
      usePartialScan: true
    });
    setLibraries([]);
    setTestResult(null);
    setShowAddModal(true);
  };

  const handleEditClick = (connection: MediaServerConnection) => {
    setEditingConnection(connection);
    setSelectedType(connection.type as ServerType);
    setFormData({
      ...connection
    });
    setLibraries([]);
    setTestResult(null);
    setShowAddModal(true);
  };

  const handleDeleteClick = (id: number) => {
    setShowDeleteConfirm(id);
  };

  const handleDeleteConfirm = async () => {
    if (!showDeleteConfirm) return;

    try {
      const response = await apiDelete(`/api/mediaserver/${showDeleteConfirm}`);
      if (response.ok) {
        setConnections(connections.filter(c => c.id !== showDeleteConfirm));
      }
    } catch (error) {
      console.error('Failed to delete connection:', error);
    } finally {
      setShowDeleteConfirm(null);
    }
  };

  const handleTypeSelect = (type: ServerType) => {
    setSelectedType(type);
    const serverInfo = serverTypes.find(s => s.type === type);
    const defaultUrl = `http://localhost:${serverInfo?.defaultPort || 8096}`;

    setFormData({
      ...formData,
      type,
      url: formData.url || defaultUrl
    });
    setLibraries([]);
    setTestResult(null);
  };

  const handleTestConnection = async () => {
    if (!formData.url || !formData.apiKey || !selectedType) return;

    setTesting(true);
    setTestResult(null);

    try {
      const response = await apiPost('/api/mediaserver/test', {
        ...formData,
        type: selectedType
      });

      const result: TestResult = await response.json();
      setTestResult(result);

      if (result.success && result.libraries) {
        setLibraries(result.libraries);
      }
    } catch (error) {
      setTestResult({
        success: false,
        message: 'Failed to test connection. Please check your settings.',
        libraries: []
      });
    } finally {
      setTesting(false);
    }
  };

  const handleSave = async () => {
    if (!formData.name || !formData.url || !formData.apiKey || !selectedType) return;

    setSaving(true);

    try {
      const payload = {
        ...formData,
        type: selectedType
      };

      let response;
      if (editingConnection) {
        response = await apiPut(`/api/mediaserver/${editingConnection.id}`, payload);
      } else {
        response = await apiPost('/api/mediaserver', payload);
      }

      if (response.ok) {
        await fetchConnections();
        setShowAddModal(false);
        setEditingConnection(null);
        setSelectedType(null);
        setFormData({
          enabled: true,
          updateLibrary: true,
          usePartialScan: true
        });
      }
    } catch (error) {
      console.error('Failed to save connection:', error);
    } finally {
      setSaving(false);
    }
  };

  const handleRefresh = async (id: number) => {
    setRefreshing(id);
    try {
      const response = await apiPost(`/api/mediaserver/${id}/refresh`, {});
      if (response.ok) {
        // Refresh the connections to get updated health status
        await fetchConnections();
      }
    } catch (error) {
      console.error('Failed to trigger refresh:', error);
    } finally {
      setRefreshing(null);
    }
  };

  const handleCloseModal = () => {
    setShowAddModal(false);
    setEditingConnection(null);
    setSelectedType(null);
    setFormData({
      enabled: true,
      updateLibrary: true,
      usePartialScan: true
    });
    setLibraries([]);
    setTestResult(null);
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-red-500"></div>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold text-white">Media Servers</h2>
          <p className="text-sm text-gray-400 mt-1">
            Connect to Plex, Jellyfin, or Emby to automatically refresh your library when new files are imported
          </p>
        </div>
        <button
          onClick={handleAddClick}
          className="flex items-center gap-2 px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
        >
          <PlusIcon className="w-5 h-5" />
          Add Connection
        </button>
      </div>

      {/* Connections List */}
      {connections.length === 0 ? (
        <div className="text-center py-12 bg-gray-800/50 rounded-lg border border-gray-700">
          <ServerIcon className="w-12 h-12 mx-auto text-gray-500 mb-4" />
          <h3 className="text-lg font-medium text-white mb-2">No Media Servers Configured</h3>
          <p className="text-gray-400 mb-4">
            Add a connection to Plex, Jellyfin, or Emby to automatically refresh your library after imports
          </p>
          <button
            onClick={handleAddClick}
            className="inline-flex items-center gap-2 px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
          >
            <PlusIcon className="w-5 h-5" />
            Add Your First Connection
          </button>
        </div>
      ) : (
        <div className="space-y-4">
          {connections.map(connection => (
            <div
              key={connection.id}
              className={`bg-gray-800 rounded-lg border ${connection.isHealthy ? 'border-gray-700' : 'border-yellow-600/50'} p-4`}
            >
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-4">
                  {/* Type Icon */}
                  <div className="text-3xl">
                    {serverTypes.find(s => s.type === connection.type)?.icon || '🖥️'}
                  </div>

                  {/* Info */}
                  <div>
                    <div className="flex items-center gap-2">
                      <h3 className="text-white font-medium">{connection.name}</h3>
                      <span className={`px-2 py-0.5 text-xs rounded ${
                        connection.enabled ? 'bg-green-900/50 text-green-400' : 'bg-gray-700 text-gray-400'
                      }`}>
                        {connection.enabled ? 'Enabled' : 'Disabled'}
                      </span>
                      {!connection.isHealthy && (
                        <span className="px-2 py-0.5 text-xs rounded bg-yellow-900/50 text-yellow-400 flex items-center gap-1">
                          <ExclamationTriangleIcon className="w-3 h-3" />
                          Unhealthy
                        </span>
                      )}
                    </div>
                    <p className="text-sm text-gray-400">
                      {connection.type} • {connection.serverName || connection.url}
                      {connection.serverVersion && ` • v${connection.serverVersion}`}
                    </p>
                    {connection.librarySectionName && (
                      <p className="text-xs text-gray-500 mt-1">
                        Library: {connection.librarySectionName}
                      </p>
                    )}
                    {connection.lastError && !connection.isHealthy && (
                      <p className="text-xs text-yellow-500 mt-1">
                        {connection.lastError}
                      </p>
                    )}
                  </div>
                </div>

                {/* Actions */}
                <div className="flex items-center gap-2">
                  <button
                    onClick={() => handleRefresh(connection.id)}
                    disabled={refreshing === connection.id}
                    className="p-2 text-gray-400 hover:text-white hover:bg-gray-700 rounded-lg transition-colors disabled:opacity-50"
                    title="Trigger library refresh"
                  >
                    <ArrowPathIcon className={`w-5 h-5 ${refreshing === connection.id ? 'animate-spin' : ''}`} />
                  </button>
                  <button
                    onClick={() => handleEditClick(connection)}
                    className="p-2 text-gray-400 hover:text-white hover:bg-gray-700 rounded-lg transition-colors"
                    title="Edit"
                  >
                    <PencilIcon className="w-5 h-5" />
                  </button>
                  <button
                    onClick={() => handleDeleteClick(connection.id)}
                    className="p-2 text-gray-400 hover:text-red-400 hover:bg-gray-700 rounded-lg transition-colors"
                    title="Delete"
                  >
                    <TrashIcon className="w-5 h-5" />
                  </button>
                </div>
              </div>

              {/* Settings Summary */}
              <div className="mt-3 pt-3 border-t border-gray-700 flex items-center gap-4 text-xs text-gray-500">
                <span className={connection.updateLibrary ? 'text-green-400' : ''}>
                  {connection.updateLibrary ? '✓' : '○'} Update Library
                </span>
                <span className={connection.usePartialScan ? 'text-green-400' : ''}>
                  {connection.usePartialScan ? '✓' : '○'} Partial Scan
                </span>
                {connection.pathMapFrom && connection.pathMapTo && (
                  <span>Path Mapping: {connection.pathMapFrom} → {connection.pathMapTo}</span>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {/* Delete Confirmation Modal */}
      {showDeleteConfirm && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div className="bg-gray-800 rounded-lg p-6 max-w-md w-full mx-4">
            <h3 className="text-lg font-semibold text-white mb-4">Delete Connection</h3>
            <p className="text-gray-400 mb-6">
              Are you sure you want to delete this media server connection? This cannot be undone.
            </p>
            <div className="flex justify-end gap-3">
              <button
                onClick={() => setShowDeleteConfirm(null)}
                className="px-4 py-2 text-gray-400 hover:text-white transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={handleDeleteConfirm}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
              >
                Delete
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Add/Edit Modal */}
      {showAddModal && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 overflow-y-auto">
          <div className="bg-gray-800 rounded-lg max-w-2xl w-full mx-4 my-8 max-h-[90vh] overflow-y-auto">
            {/* Modal Header */}
            <div className="sticky top-0 bg-gray-800 border-b border-gray-700 px-6 py-4 flex items-center justify-between">
              <h3 className="text-lg font-semibold text-white">
                {editingConnection ? 'Edit Media Server' : 'Add Media Server'}
              </h3>
              <button
                onClick={handleCloseModal}
                className="p-2 text-gray-400 hover:text-white hover:bg-gray-700 rounded-lg transition-colors"
              >
                <XMarkIcon className="w-5 h-5" />
              </button>
            </div>

            {/* Modal Content */}
            <div className="p-6 space-y-6">
              {/* Step 1: Select Type (if not editing) */}
              {!editingConnection && !selectedType && (
                <div className="space-y-4">
                  <h4 className="text-sm font-medium text-gray-300">Select Media Server Type</h4>
                  <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
                    {serverTypes.map(server => (
                      <button
                        key={server.type}
                        onClick={() => handleTypeSelect(server.type)}
                        className="p-4 bg-gray-700 hover:bg-gray-600 rounded-lg text-left transition-colors"
                      >
                        <div className="text-3xl mb-2">{server.icon}</div>
                        <h5 className="text-white font-medium">{server.name}</h5>
                        <p className="text-xs text-gray-400 mt-1">{server.description}</p>
                      </button>
                    ))}
                  </div>
                </div>
              )}

              {/* Step 2: Configuration Form */}
              {(selectedType || editingConnection) && (
                <div className="space-y-6">
                  {/* Selected Type Display */}
                  <div className="flex items-center gap-3 pb-4 border-b border-gray-700">
                    <div className="text-3xl">
                      {serverTypes.find(s => s.type === selectedType)?.icon || '🖥️'}
                    </div>
                    <div>
                      <h4 className="text-white font-medium">{selectedType}</h4>
                      <p className="text-xs text-gray-400">
                        {serverTypes.find(s => s.type === selectedType)?.description}
                      </p>
                    </div>
                    {!editingConnection && (
                      <button
                        onClick={() => setSelectedType(null)}
                        className="ml-auto text-sm text-gray-400 hover:text-white"
                      >
                        Change
                      </button>
                    )}
                  </div>

                  {/* Name */}
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">Name</label>
                    <input
                      type="text"
                      value={formData.name || ''}
                      onChange={e => setFormData({ ...formData, name: e.target.value })}
                      placeholder={`My ${selectedType} Server`}
                      className="w-full px-4 py-2 bg-gray-700 border border-gray-600 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-red-500"
                    />
                  </div>

                  {/* URL */}
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">Server URL</label>
                    <input
                      type="text"
                      value={formData.url || ''}
                      onChange={e => setFormData({ ...formData, url: e.target.value })}
                      placeholder={`http://localhost:${serverTypes.find(s => s.type === selectedType)?.defaultPort || 8096}`}
                      className="w-full px-4 py-2 bg-gray-700 border border-gray-600 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-red-500"
                    />
                    <p className="text-xs text-gray-500 mt-1">
                      {selectedType === 'Plex'
                        ? 'Usually http://localhost:32400 or your Plex server IP'
                        : 'Usually http://localhost:8096 or your server IP'
                      }
                    </p>
                  </div>

                  {/* API Key / Token */}
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">
                      {selectedType === 'Plex' ? 'Plex Token' : 'API Key'}
                    </label>
                    <input
                      type="password"
                      value={formData.apiKey || ''}
                      onChange={e => setFormData({ ...formData, apiKey: e.target.value })}
                      placeholder={selectedType === 'Plex' ? 'X-Plex-Token' : 'API Key'}
                      className="w-full px-4 py-2 bg-gray-700 border border-gray-600 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-red-500"
                    />
                    <p className="text-xs text-gray-500 mt-1">
                      {selectedType === 'Plex'
                        ? 'Find your token at plex.tv/claim or in Plex settings'
                        : `Generate an API key in ${selectedType}'s admin dashboard under Advanced > API Keys`
                      }
                    </p>
                  </div>

                  {/* Test Connection Button */}
                  <div>
                    <button
                      onClick={handleTestConnection}
                      disabled={testing || !formData.url || !formData.apiKey}
                      className="flex items-center gap-2 px-4 py-2 bg-blue-600 hover:bg-blue-700 disabled:bg-gray-600 disabled:cursor-not-allowed text-white rounded-lg transition-colors"
                    >
                      {testing ? (
                        <>
                          <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white"></div>
                          Testing...
                        </>
                      ) : (
                        <>
                          <ArrowPathIcon className="w-4 h-4" />
                          Test Connection
                        </>
                      )}
                    </button>

                    {/* Test Result */}
                    {testResult && (
                      <div className={`mt-3 p-3 rounded-lg ${testResult.success ? 'bg-green-900/30 border border-green-700' : 'bg-red-900/30 border border-red-700'}`}>
                        <div className="flex items-center gap-2">
                          {testResult.success ? (
                            <CheckCircleIcon className="w-5 h-5 text-green-500" />
                          ) : (
                            <ExclamationTriangleIcon className="w-5 h-5 text-red-500" />
                          )}
                          <span className={testResult.success ? 'text-green-400' : 'text-red-400'}>
                            {testResult.message}
                          </span>
                        </div>
                        {testResult.serverName && (
                          <p className="text-sm text-gray-400 mt-2">
                            Server: {testResult.serverName}
                            {testResult.serverVersion && ` v${testResult.serverVersion}`}
                          </p>
                        )}
                      </div>
                    )}
                  </div>

                  {/* Library Selection (shown after successful test) */}
                  {libraries.length > 0 && (
                    <div>
                      <label className="block text-sm font-medium text-gray-300 mb-2">
                        Library to Update (Optional)
                      </label>
                      <div className="relative">
                        <select
                          value={formData.librarySectionId || ''}
                          onChange={e => {
                            const selectedLib = libraries.find(l => l.id === e.target.value);
                            setFormData({
                              ...formData,
                              librarySectionId: e.target.value || undefined,
                              librarySectionName: selectedLib?.name || undefined
                            });
                          }}
                          className="w-full px-4 py-2 bg-gray-700 border border-gray-600 rounded-lg text-white focus:outline-none focus:border-red-500 appearance-none"
                        >
                          <option value="">Auto-detect (scan all matching)</option>
                          {libraries.map(lib => (
                            <option key={lib.id} value={lib.id}>
                              {lib.name} ({lib.type})
                              {lib.path && ` - ${lib.path}`}
                            </option>
                          ))}
                        </select>
                        <ChevronDownIcon className="w-5 h-5 text-gray-400 absolute right-3 top-1/2 -translate-y-1/2 pointer-events-none" />
                      </div>
                      <p className="text-xs text-gray-500 mt-1">
                        Select a specific library or leave on auto-detect to refresh all matching libraries
                      </p>
                    </div>
                  )}

                  {/* Options */}
                  <div className="space-y-4">
                    <h4 className="text-sm font-medium text-gray-300">Options</h4>

                    {/* Enabled */}
                    <label className="flex items-center gap-3 cursor-pointer">
                      <input
                        type="checkbox"
                        checked={formData.enabled ?? true}
                        onChange={e => setFormData({ ...formData, enabled: e.target.checked })}
                        className="w-4 h-4 rounded border-gray-600 bg-gray-700 text-red-600 focus:ring-red-500"
                      />
                      <div>
                        <span className="text-white">Enabled</span>
                        <p className="text-xs text-gray-500">Enable or disable this connection</p>
                      </div>
                    </label>

                    {/* Update Library */}
                    <label className="flex items-center gap-3 cursor-pointer">
                      <input
                        type="checkbox"
                        checked={formData.updateLibrary ?? true}
                        onChange={e => setFormData({ ...formData, updateLibrary: e.target.checked })}
                        className="w-4 h-4 rounded border-gray-600 bg-gray-700 text-red-600 focus:ring-red-500"
                      />
                      <div>
                        <span className="text-white">Update Library on Import</span>
                        <p className="text-xs text-gray-500">Trigger library refresh when files are imported</p>
                      </div>
                    </label>

                    {/* Partial Scan */}
                    <label className="flex items-center gap-3 cursor-pointer">
                      <input
                        type="checkbox"
                        checked={formData.usePartialScan ?? true}
                        onChange={e => setFormData({ ...formData, usePartialScan: e.target.checked })}
                        className="w-4 h-4 rounded border-gray-600 bg-gray-700 text-red-600 focus:ring-red-500"
                      />
                      <div>
                        <span className="text-white">Use Partial Scan</span>
                        <p className="text-xs text-gray-500">Only scan the specific folder (faster). Disable to scan entire library.</p>
                      </div>
                    </label>
                  </div>

                  {/* Path Mapping (Advanced) */}
                  {showAdvanced && (
                    <div className="space-y-4 pt-4 border-t border-gray-700">
                      <h4 className="text-sm font-medium text-gray-300">Path Mapping</h4>
                      <p className="text-xs text-gray-500">
                        If your media server sees different paths than Sportarr (common in Docker), configure path mapping here.
                      </p>

                      <div className="grid grid-cols-2 gap-4">
                        <div>
                          <label className="block text-sm text-gray-400 mb-1">Sportarr Path</label>
                          <input
                            type="text"
                            value={formData.pathMapFrom || ''}
                            onChange={e => setFormData({ ...formData, pathMapFrom: e.target.value })}
                            placeholder="/sports"
                            className="w-full px-4 py-2 bg-gray-700 border border-gray-600 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-red-500"
                          />
                        </div>
                        <div>
                          <label className="block text-sm text-gray-400 mb-1">Media Server Path</label>
                          <input
                            type="text"
                            value={formData.pathMapTo || ''}
                            onChange={e => setFormData({ ...formData, pathMapTo: e.target.value })}
                            placeholder="/data/media/sports"
                            className="w-full px-4 py-2 bg-gray-700 border border-gray-600 rounded-lg text-white placeholder-gray-500 focus:outline-none focus:border-red-500"
                          />
                        </div>
                      </div>
                    </div>
                  )}
                </div>
              )}
            </div>

            {/* Modal Footer */}
            {(selectedType || editingConnection) && (
              <div className="sticky bottom-0 bg-gray-800 border-t border-gray-700 px-6 py-4 flex justify-end gap-3">
                <button
                  onClick={handleCloseModal}
                  className="px-4 py-2 text-gray-400 hover:text-white transition-colors"
                >
                  Cancel
                </button>
                <button
                  onClick={handleSave}
                  disabled={saving || !formData.name || !formData.url || !formData.apiKey}
                  className="flex items-center gap-2 px-4 py-2 bg-red-600 hover:bg-red-700 disabled:bg-gray-600 disabled:cursor-not-allowed text-white rounded-lg transition-colors"
                >
                  {saving ? (
                    <>
                      <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white"></div>
                      Saving...
                    </>
                  ) : (
                    editingConnection ? 'Save Changes' : 'Add Connection'
                  )}
                </button>
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
