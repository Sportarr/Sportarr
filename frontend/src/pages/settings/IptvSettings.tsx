import { useState, useMemo, useEffect } from 'react';
import {
  PlusIcon,
  PencilIcon,
  TrashIcon,
  CheckCircleIcon,
  XCircleIcon,
  SignalIcon,
  XMarkIcon,
  ArrowPathIcon,
  PlayIcon,
  ListBulletIcon,
  BoltIcon,
} from '@heroicons/react/24/outline';
import { toast } from 'sonner';
import apiClient from '../../api/client';
import SettingsHeader from '../../components/SettingsHeader';
import StreamPlayerModal from '../../components/StreamPlayerModal';

// IPTV Source Types
type IptvSourceType = 'M3U' | 'Xtream';

interface IptvSource {
  id: number;
  name: string;
  type: IptvSourceType;
  url: string;
  username?: string;
  password?: string;
  maxStreams?: number;
  userAgent?: string;
  isActive: boolean;
  channelCount: number;
  lastUpdated?: string;
  lastError?: string;
}

interface IptvChannel {
  id: number;
  name: string;
  channelNumber: number;
  streamUrl: string;
  logoUrl?: string;
  group?: string;
  tvgId?: string;
  isSportsChannel: boolean;
  status: 'Unknown' | 'Online' | 'Offline' | 'Error';
  isEnabled: boolean;
}

interface ChannelStats {
  totalCount: number;
  sportsCount: number;
  onlineCount: number;
  offlineCount: number;
  unknownCount: number;
  enabledCount: number;
  groupCount: number;
}

// Form state for adding/editing sources
interface SourceFormData {
  name: string;
  type: IptvSourceType;
  url: string;
  username: string;
  password: string;
  maxStreams: number;
  userAgent: string;
}

const defaultFormData: SourceFormData = {
  name: '',
  type: 'M3U',
  url: '',
  username: '',
  password: '',
  maxStreams: 1,
  userAgent: '',
};

export default function IptvSettings() {
  // State
  const [sources, setSources] = useState<IptvSource[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Modal state
  const [showAddModal, setShowAddModal] = useState(false);
  const [editingSource, setEditingSource] = useState<IptvSource | null>(null);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState<number | null>(null);
  const [formData, setFormData] = useState<SourceFormData>(defaultFormData);

  // Channel viewer state
  const [viewingSource, setViewingSource] = useState<IptvSource | null>(null);
  const [channels, setChannels] = useState<IptvChannel[]>([]);
  const [channelStats, setChannelStats] = useState<ChannelStats | null>(null);
  const [channelSearch, setChannelSearch] = useState('');
  const [channelFilter, setChannelFilter] = useState<'all' | 'sports'>('sports');
  const [groups, setGroups] = useState<string[]>([]);
  const [selectedGroup, setSelectedGroup] = useState<string>('');
  const [channelPage, setChannelPage] = useState(0);
  const [hasMoreChannels, setHasMoreChannels] = useState(true);
  const [loadingChannels, setLoadingChannels] = useState(false);
  const CHANNEL_PAGE_SIZE = 100;

  // Testing state
  const [isTesting, setIsTesting] = useState(false);
  const [testResult, setTestResult] = useState<{ success: boolean; message: string; channelCount?: number } | null>(null);

  // Syncing state
  const [syncingSourceId, setSyncingSourceId] = useState<number | null>(null);

  // Stream player state
  const [playerChannel, setPlayerChannel] = useState<IptvChannel | null>(null);

  // Load sources on mount
  useEffect(() => {
    loadSources();
  }, []);

  const loadSources = async () => {
    try {
      setIsLoading(true);
      const { data } = await apiClient.get<IptvSource[]>('/iptv/sources');
      setSources(data);
    } catch (err: any) {
      setError(err.message || 'Failed to load IPTV sources');
    } finally {
      setIsLoading(false);
    }
  };

  const loadChannels = async (sourceId: number, page: number = 0, reset: boolean = true) => {
    try {
      setLoadingChannels(true);
      const offset = page * CHANNEL_PAGE_SIZE;

      // Only load stats and groups on first page
      const requests: Promise<any>[] = [
        apiClient.get<IptvChannel[]>(`/iptv/sources/${sourceId}/channels`, {
          params: {
            sportsOnly: channelFilter === 'sports' ? true : undefined,
            search: channelSearch || undefined,
            group: selectedGroup || undefined,
            limit: CHANNEL_PAGE_SIZE,
            offset,
          },
        }),
      ];

      if (page === 0) {
        requests.push(
          apiClient.get<ChannelStats>(`/iptv/sources/${sourceId}/stats`),
          apiClient.get<string[]>(`/iptv/sources/${sourceId}/groups`)
        );
      }

      const results = await Promise.all(requests);
      const channelsData = Array.isArray(results[0].data) ? results[0].data : [];

      if (reset) {
        setChannels(channelsData);
      } else {
        setChannels(prev => [...prev, ...channelsData]);
      }

      setChannelPage(page);
      setHasMoreChannels(channelsData.length === CHANNEL_PAGE_SIZE);

      if (page === 0 && results.length > 1) {
        setChannelStats(results[1].data);
        setGroups(Array.isArray(results[2].data) ? results[2].data : []);
      }
    } catch (err: any) {
      toast.error('Failed to load channels', { description: err.message });
    } finally {
      setLoadingChannels(false);
    }
  };

  const handleFormChange = (field: keyof SourceFormData, value: any) => {
    setFormData(prev => ({ ...prev, [field]: value }));
  };

  const handleAddSource = async () => {
    try {
      setError(null);
      const response = await apiClient.post<IptvSource>('/iptv/sources', formData);
      setSources(prev => [...prev, response.data]);
      setShowAddModal(false);
      setFormData(defaultFormData);
      toast.success('Source Added', { description: `${formData.name} has been added and channels synced` });
    } catch (err: any) {
      setError(err.message || 'Failed to add source');
      toast.error('Failed to add source', { description: err.message });
    }
  };

  const handleUpdateSource = async () => {
    if (!editingSource) return;
    try {
      setError(null);
      const response = await apiClient.put<IptvSource>(`/iptv/sources/${editingSource.id}`, formData);
      setSources(prev => prev.map(s => s.id === editingSource.id ? response.data : s));
      setEditingSource(null);
      setFormData(defaultFormData);
      toast.success('Source Updated', { description: `${formData.name} has been updated` });
    } catch (err: any) {
      setError(err.message || 'Failed to update source');
      toast.error('Failed to update source', { description: err.message });
    }
  };

  const handleDeleteSource = async (id: number) => {
    try {
      setError(null);
      await apiClient.delete(`/iptv/sources/${id}`);
      setSources(prev => prev.filter(s => s.id !== id));
      setShowDeleteConfirm(null);
      toast.success('Source Deleted');
    } catch (err: any) {
      setError(err.message || 'Failed to delete source');
      toast.error('Failed to delete source', { description: err.message });
    }
  };

  const handleToggleActive = async (source: IptvSource) => {
    try {
      const response = await apiClient.post<IptvSource>(`/iptv/sources/${source.id}/toggle`);
      setSources(prev => prev.map(s => s.id === source.id ? response.data : s));
      toast.success(response.data.isActive ? 'Source Enabled' : 'Source Disabled');
    } catch (err: any) {
      toast.error('Failed to toggle source', { description: err.message });
    }
  };

  const handleSyncChannels = async (sourceId: number) => {
    try {
      setSyncingSourceId(sourceId);
      const response = await apiClient.post<{ channelCount: number }>(`/iptv/sources/${sourceId}/sync`);
      await loadSources();
      toast.success('Channels Synced', { description: `Synced ${response.data.channelCount} channels` });
    } catch (err: any) {
      toast.error('Failed to sync channels', { description: err.message });
    } finally {
      setSyncingSourceId(null);
    }
  };

  const handleTestSource = async () => {
    try {
      setIsTesting(true);
      setTestResult(null);
      const response = await apiClient.post<{ success: boolean; error?: string; channelCount?: number }>('/iptv/sources/test', formData);
      if (response.data.success) {
        setTestResult({
          success: true,
          message: `Connection successful! Found ${response.data.channelCount || 0} channels.`,
          channelCount: response.data.channelCount,
        });
        toast.success('Test Successful');
      } else {
        setTestResult({ success: false, message: response.data.error || 'Connection failed' });
        toast.error('Test Failed', { description: response.data.error });
      }
    } catch (err: any) {
      setTestResult({ success: false, message: err.message || 'Connection test failed' });
      toast.error('Test Failed', { description: err.message });
    } finally {
      setIsTesting(false);
    }
  };

  // Testing channel state
  const [testingChannelId, setTestingChannelId] = useState<number | null>(null);

  const handleTestChannel = async (channelId: number) => {
    try {
      setTestingChannelId(channelId);
      const response = await apiClient.post<{ success: boolean; error?: string }>(`/iptv/channels/${channelId}/test`);
      // Update the channel status immediately in state
      setChannels((prev) =>
        prev.map((c) =>
          c.id === channelId
            ? { ...c, status: response.data.success ? 'Online' : 'Offline' }
            : c
        )
      );
      if (response.data.success) {
        toast.success('Channel Online');
      } else {
        toast.error('Channel Offline', { description: response.data.error });
      }
    } catch (err: any) {
      toast.error('Failed to test channel', { description: err.message });
    } finally {
      setTestingChannelId(null);
    }
  };

  const handleEditSource = (source: IptvSource) => {
    setEditingSource(source);
    setFormData({
      name: source.name,
      type: source.type,
      url: source.url,
      username: source.username || '',
      password: source.password || '',
      maxStreams: source.maxStreams || 1,
      userAgent: source.userAgent || '',
    });
  };

  const handleViewChannels = async (source: IptvSource) => {
    setViewingSource(source);
    setChannelSearch('');
    setChannelFilter('sports');
    setSelectedGroup('');
    setChannelPage(0);
    setHasMoreChannels(true);
    await loadChannels(source.id, 0, true);
  };

  const handleCancelEdit = () => {
    setShowAddModal(false);
    setEditingSource(null);
    setFormData(defaultFormData);
    setTestResult(null);
  };

  // Filter channels based on search/filter
  const filteredChannels = useMemo(() => {
    return channels;
  }, [channels]);

  const renderSourceForm = () => {
    const isXtream = formData.type === 'Xtream';

    return (
      <div className="space-y-6">
        {/* Basic Settings */}
        <div className="space-y-4">
          <h4 className="text-lg font-semibold text-white">Source Settings</h4>

          <div>
            <label className="block text-sm font-medium text-gray-300 mb-2">Name *</label>
            <input
              type="text"
              value={formData.name}
              onChange={(e) => handleFormChange('name', e.target.value)}
              className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
              placeholder="My IPTV Provider"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-300 mb-2">Type *</label>
            <select
              value={formData.type}
              onChange={(e) => handleFormChange('type', e.target.value as IptvSourceType)}
              className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
            >
              <option value="M3U">M3U Playlist</option>
              <option value="Xtream">Xtream Codes API</option>
            </select>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-300 mb-2">
              {isXtream ? 'Server URL *' : 'Playlist URL *'}
            </label>
            <input
              type="text"
              value={formData.url}
              onChange={(e) => handleFormChange('url', e.target.value)}
              className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
              placeholder={isXtream ? 'http://server.example.com:8080' : 'http://example.com/playlist.m3u'}
            />
            <p className="text-xs text-gray-500 mt-1">
              {isXtream
                ? 'The base URL of your Xtream Codes server (without /player_api.php)'
                : 'Direct URL to your M3U/M3U8 playlist file'}
            </p>
          </div>

          {isXtream && (
            <>
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Username *</label>
                <input
                  type="text"
                  value={formData.username}
                  onChange={(e) => handleFormChange('username', e.target.value)}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                  placeholder="Your username"
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Password *</label>
                <input
                  type="password"
                  value={formData.password}
                  onChange={(e) => handleFormChange('password', e.target.value)}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                  placeholder="Your password"
                />
              </div>
            </>
          )}
        </div>

        {/* Advanced Settings */}
        <div className="space-y-4">
          <h4 className="text-lg font-semibold text-white">Advanced Settings</h4>

          <div>
            <label className="block text-sm font-medium text-gray-300 mb-2">Max Concurrent Streams</label>
            <input
              type="number"
              value={formData.maxStreams}
              onChange={(e) => handleFormChange('maxStreams', parseInt(e.target.value) || 1)}
              min="1"
              max="10"
              className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
            />
            <p className="text-xs text-gray-500 mt-1">
              Maximum number of simultaneous recordings from this source
            </p>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-300 mb-2">Custom User Agent</label>
            <input
              type="text"
              value={formData.userAgent}
              onChange={(e) => handleFormChange('userAgent', e.target.value)}
              className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
              placeholder="Leave empty for default (VLC/3.0.18)"
            />
            <p className="text-xs text-gray-500 mt-1">
              Some providers require a specific user agent string
            </p>
          </div>
        </div>

        {/* Test Result */}
        {testResult && (
          <div className={`p-4 rounded-lg ${testResult.success ? 'bg-green-950/30 border border-green-900/50' : 'bg-red-950/30 border border-red-900/50'}`}>
            <div className="flex items-center">
              {testResult.success ? (
                <CheckCircleIcon className="w-5 h-5 text-green-400 mr-2" />
              ) : (
                <XCircleIcon className="w-5 h-5 text-red-400 mr-2" />
              )}
              <span className={testResult.success ? 'text-green-400' : 'text-red-400'}>
                {testResult.message}
              </span>
            </div>
          </div>
        )}
      </div>
    );
  };

  const isFormValid = () => {
    if (!formData.name.trim() || !formData.url.trim()) return false;
    if (formData.type === 'Xtream' && (!formData.username.trim() || !formData.password.trim())) return false;
    return true;
  };

  return (
    <div>
      <SettingsHeader
        title="IPTV Sources"
        subtitle="Configure IPTV sources for DVR recording of sports events"
        showSaveButton={false}
      />

      <div className="max-w-6xl mx-auto px-6">
        {/* Error Alert */}
        {error && (
          <div className="mb-6 bg-red-950/30 border border-red-900/50 rounded-lg p-4 flex items-start">
            <XCircleIcon className="w-6 h-6 text-red-400 mr-3 flex-shrink-0 mt-0.5" />
            <div className="flex-1">
              <h3 className="text-lg font-semibold text-red-400 mb-1">Error</h3>
              <p className="text-sm text-gray-300">{error}</p>
            </div>
            <button
              onClick={() => setError(null)}
              className="text-gray-400 hover:text-white ml-4"
            >
              <XMarkIcon className="w-5 h-5" />
            </button>
          </div>
        )}

        {/* Info Box */}
        <div className="mb-8 bg-blue-950/30 border border-blue-900/50 rounded-lg p-6">
          <div className="flex items-start">
            <SignalIcon className="w-6 h-6 text-blue-400 mr-3 flex-shrink-0 mt-0.5" />
            <div>
              <h3 className="text-lg font-semibold text-white mb-2">About IPTV Sources</h3>
              <ul className="space-y-2 text-sm text-gray-300">
                <li className="flex items-start">
                  <span className="text-red-400 mr-2">*</span>
                  <span>
                    <strong>M3U Playlists:</strong> Standard playlist format supported by most IPTV providers
                  </span>
                </li>
                <li className="flex items-start">
                  <span className="text-red-400 mr-2">*</span>
                  <span>
                    <strong>Xtream Codes:</strong> API-based access with automatic EPG and category support
                  </span>
                </li>
                <li className="flex items-start">
                  <span className="text-red-400 mr-2">*</span>
                  <span>
                    Sports channels are automatically detected and can be mapped to leagues for DVR scheduling
                  </span>
                </li>
                <li className="flex items-start">
                  <span className="text-red-400 mr-2">*</span>
                  <span>
                    Channel testing verifies stream connectivity before recording
                  </span>
                </li>
              </ul>
            </div>
          </div>
        </div>

        {/* Sources List */}
        <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
          <div className="flex items-center justify-between mb-6">
            <h3 className="text-xl font-semibold text-white">Your IPTV Sources</h3>
            <button
              onClick={() => setShowAddModal(true)}
              className="flex items-center px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
            >
              <PlusIcon className="w-4 h-4 mr-2" />
              Add Source
            </button>
          </div>

          <div className="space-y-3">
            {sources.map((source) => (
              <div
                key={source.id}
                className="group bg-black/30 border border-gray-800 hover:border-red-900/50 rounded-lg p-4 transition-all"
              >
                <div className="flex items-start justify-between">
                  <div className="flex items-start space-x-4 flex-1">
                    {/* Status Icon */}
                    <div className="mt-1">
                      {source.isActive ? (
                        <CheckCircleIcon className="w-6 h-6 text-green-500" />
                      ) : (
                        <XCircleIcon className="w-6 h-6 text-gray-500" />
                      )}
                    </div>

                    {/* Source Info */}
                    <div className="flex-1">
                      <div className="flex items-center space-x-3 mb-2">
                        <h4 className="text-lg font-semibold text-white">{source.name}</h4>
                        <span className={`px-2 py-0.5 text-xs rounded ${
                          source.type === 'M3U'
                            ? 'bg-blue-900/30 text-blue-400'
                            : 'bg-purple-900/30 text-purple-400'
                        }`}>
                          {source.type}
                        </span>
                        <span className="px-2 py-0.5 bg-gray-800 text-gray-400 text-xs rounded">
                          {source.channelCount} channels
                        </span>
                      </div>

                      <div className="space-y-1 text-sm text-gray-400">
                        <p>
                          <span className="text-gray-500">URL:</span>{' '}
                          <span className="text-white truncate">{source.url}</span>
                        </p>
                        {source.lastUpdated && (
                          <p>
                            <span className="text-gray-500">Last Synced:</span>{' '}
                            <span className="text-white">
                              {new Date(source.lastUpdated).toLocaleString()}
                            </span>
                          </p>
                        )}
                        {source.lastError && (
                          <p className="text-red-400">
                            <span className="text-gray-500">Error:</span> {source.lastError}
                          </p>
                        )}
                      </div>
                    </div>
                  </div>

                  {/* Actions */}
                  <div className="flex items-center space-x-2 ml-4">
                    <button
                      onClick={() => handleViewChannels(source)}
                      className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                      title="View Channels"
                    >
                      <ListBulletIcon className="w-5 h-5" />
                    </button>
                    <button
                      onClick={() => handleSyncChannels(source.id)}
                      disabled={syncingSourceId === source.id}
                      className={`p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors ${
                        syncingSourceId === source.id ? 'animate-spin' : ''
                      }`}
                      title="Sync Channels"
                    >
                      <ArrowPathIcon className="w-5 h-5" />
                    </button>
                    <button
                      onClick={() => handleToggleActive(source)}
                      className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                      title={source.isActive ? 'Disable' : 'Enable'}
                    >
                      {source.isActive ? (
                        <CheckCircleIcon className="w-5 h-5 text-green-400" />
                      ) : (
                        <XCircleIcon className="w-5 h-5" />
                      )}
                    </button>
                    <button
                      onClick={() => handleEditSource(source)}
                      className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                      title="Edit"
                    >
                      <PencilIcon className="w-5 h-5" />
                    </button>
                    <button
                      onClick={() => setShowDeleteConfirm(source.id)}
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

          {isLoading && (
            <div className="text-center py-12">
              <div className="animate-spin rounded-full h-16 w-16 border-b-2 border-red-600 mx-auto mb-4"></div>
              <p className="text-gray-500">Loading IPTV sources...</p>
            </div>
          )}

          {!isLoading && sources.length === 0 && (
            <div className="text-center py-12">
              <SignalIcon className="w-16 h-16 text-gray-700 mx-auto mb-4" />
              <p className="text-gray-500 mb-2">No IPTV sources configured</p>
              <p className="text-sm text-gray-400 mb-4">
                Add your M3U playlist or Xtream Codes credentials to get started
              </p>
            </div>
          )}
        </div>

        {/* Add/Edit Modal */}
        {(showAddModal || editingSource) && (
          <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4 overflow-y-auto">
            <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-2xl w-full my-8">
              <div className="flex items-center justify-between mb-6">
                <h3 className="text-2xl font-bold text-white">
                  {editingSource ? `Edit ${editingSource.name}` : 'Add IPTV Source'}
                </h3>
                <button
                  onClick={handleCancelEdit}
                  className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                >
                  <XMarkIcon className="w-6 h-6" />
                </button>
              </div>

              <div className="max-h-[60vh] overflow-y-auto pr-2">
                {renderSourceForm()}
              </div>

              <div className="mt-6 pt-6 border-t border-gray-800 flex items-center justify-end space-x-3">
                <button
                  onClick={handleCancelEdit}
                  className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
                >
                  Cancel
                </button>
                <button
                  onClick={handleTestSource}
                  disabled={!isFormValid() || isTesting}
                  className={`px-4 py-2 rounded-lg transition-colors ${
                    isFormValid() && !isTesting
                      ? 'bg-blue-600 hover:bg-blue-700 text-white'
                      : 'bg-gray-700 text-gray-500 cursor-not-allowed'
                  }`}
                >
                  {isTesting ? 'Testing...' : 'Test'}
                </button>
                <button
                  onClick={editingSource ? handleUpdateSource : handleAddSource}
                  disabled={!isFormValid()}
                  className={`px-4 py-2 rounded-lg transition-colors ${
                    isFormValid()
                      ? 'bg-red-600 hover:bg-red-700 text-white'
                      : 'bg-gray-700 text-gray-500 cursor-not-allowed'
                  }`}
                >
                  {editingSource ? 'Save' : 'Add'}
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Delete Confirmation Modal */}
        {showDeleteConfirm !== null && (
          <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
            <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-md w-full">
              <h3 className="text-2xl font-bold text-white mb-4">Delete Source?</h3>
              <p className="text-gray-400 mb-6">
                Are you sure you want to delete this IPTV source? This will also remove all associated channels and mappings. This action cannot be undone.
              </p>
              <div className="flex items-center justify-end space-x-3">
                <button
                  onClick={() => setShowDeleteConfirm(null)}
                  className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
                >
                  Cancel
                </button>
                <button
                  onClick={() => handleDeleteSource(showDeleteConfirm)}
                  className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
                >
                  Delete
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Stream Player Modal */}
        <StreamPlayerModal
          isOpen={!!playerChannel}
          onClose={() => setPlayerChannel(null)}
          streamUrl={playerChannel?.streamUrl || null}
          channelId={playerChannel?.id}
          channelName={playerChannel?.name || ''}
        />

        {/* Channel Viewer Modal */}
        {viewingSource && (
          <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4 overflow-y-auto">
            <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-5xl w-full my-8">
              <div className="flex items-center justify-between mb-6">
                <div>
                  <h3 className="text-2xl font-bold text-white">{viewingSource.name} Channels</h3>
                  {channelStats && (
                    <div className="flex items-center space-x-4 mt-2 text-sm text-gray-400">
                      <span>{channelStats.totalCount} total</span>
                      <span className="text-green-400">{channelStats.sportsCount} sports</span>
                      <span className="text-blue-400">{channelStats.onlineCount} online</span>
                      <span className="text-red-400">{channelStats.offlineCount} offline</span>
                    </div>
                  )}
                </div>
                <button
                  onClick={() => setViewingSource(null)}
                  className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                >
                  <XMarkIcon className="w-6 h-6" />
                </button>
              </div>

              {/* Filters */}
              <div className="flex items-center space-x-4 mb-6">
                <div className="flex-1">
                  <input
                    type="text"
                    value={channelSearch}
                    onChange={(e) => {
                      setChannelSearch(e.target.value);
                    }}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter') {
                        loadChannels(viewingSource.id, 0, true);
                      }
                    }}
                    className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    placeholder="Search channels..."
                  />
                </div>
                <select
                  value={channelFilter}
                  onChange={(e) => {
                    setChannelFilter(e.target.value as 'all' | 'sports');
                    loadChannels(viewingSource.id, 0, true);
                  }}
                  className="px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                >
                  <option value="all">All Channels</option>
                  <option value="sports">Sports Only</option>
                </select>
                {groups.length > 0 && (
                  <select
                    value={selectedGroup}
                    onChange={(e) => {
                      setSelectedGroup(e.target.value);
                      loadChannels(viewingSource.id, 0, true);
                    }}
                    className="px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                  >
                    <option value="">All Groups</option>
                    {groups.map((group) => (
                      <option key={group} value={group}>{group}</option>
                    ))}
                  </select>
                )}
              </div>

              {/* Channel List */}
              <div className="max-h-[50vh] overflow-y-auto space-y-2">
                {filteredChannels.map((channel) => (
                  <div
                    key={channel.id}
                    className="flex items-center justify-between bg-black/30 border border-gray-800 rounded-lg p-3"
                  >
                    <div className="flex items-center space-x-3">
                      {channel.logoUrl ? (
                        <img
                          src={channel.logoUrl}
                          alt={channel.name}
                          className="w-8 h-8 rounded object-contain bg-gray-800"
                          onError={(e) => {
                            (e.target as HTMLImageElement).style.display = 'none';
                          }}
                        />
                      ) : (
                        <div className="w-8 h-8 rounded bg-gray-800 flex items-center justify-center">
                          <SignalIcon className="w-4 h-4 text-gray-600" />
                        </div>
                      )}
                      <div>
                        <div className="flex items-center space-x-2">
                          <span className="text-white font-medium">{channel.name}</span>
                          {channel.isSportsChannel && (
                            <span className="px-1.5 py-0.5 bg-green-900/30 text-green-400 text-xs rounded">
                              Sports
                            </span>
                          )}
                        </div>
                        {channel.group && (
                          <span className="text-xs text-gray-500">{channel.group}</span>
                        )}
                      </div>
                    </div>
                    <div className="flex items-center space-x-2">
                      <span className={`px-2 py-0.5 text-xs rounded ${
                        channel.status === 'Online' ? 'bg-green-900/30 text-green-400' :
                        channel.status === 'Offline' ? 'bg-red-900/30 text-red-400' :
                        channel.status === 'Error' ? 'bg-yellow-900/30 text-yellow-400' :
                        'bg-gray-800 text-gray-400'
                      }`}>
                        {channel.status}
                      </span>
                      <button
                        onClick={() => handleTestChannel(channel.id)}
                        disabled={testingChannelId === channel.id}
                        className={`p-1.5 text-gray-400 hover:text-green-400 hover:bg-gray-800 rounded transition-colors ${
                          testingChannelId === channel.id ? 'animate-pulse' : ''
                        }`}
                        title="Test Connection"
                      >
                        <BoltIcon className="w-4 h-4" />
                      </button>
                      <button
                        onClick={() => setPlayerChannel(channel)}
                        className="p-1.5 text-gray-400 hover:text-blue-400 hover:bg-gray-800 rounded transition-colors"
                        title="Play Stream"
                      >
                        <PlayIcon className="w-4 h-4" />
                      </button>
                    </div>
                  </div>
                ))}
                {filteredChannels.length === 0 && !loadingChannels && (
                  <div className="text-center py-8 text-gray-500">
                    No channels found
                  </div>
                )}
                {loadingChannels && (
                  <div className="text-center py-4">
                    <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-red-600 mx-auto"></div>
                  </div>
                )}
                {hasMoreChannels && !loadingChannels && filteredChannels.length > 0 && (
                  <div className="text-center py-4">
                    <button
                      onClick={() => loadChannels(viewingSource.id, channelPage + 1, false)}
                      className="px-4 py-2 bg-red-900/30 hover:bg-red-900/50 text-red-400 rounded-lg transition-colors"
                    >
                      Load More Channels
                    </button>
                  </div>
                )}
              </div>

              <div className="mt-6 pt-6 border-t border-gray-800 flex items-center justify-between">
                <span className="text-sm text-gray-500">
                  Showing {channels.length} channels{channelStats ? ` of ${channelStats.totalCount} total` : ''}
                </span>
                <button
                  onClick={() => setViewingSource(null)}
                  className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
                >
                  Close
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
