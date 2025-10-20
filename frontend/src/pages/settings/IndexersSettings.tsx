import { useState, useMemo } from 'react';
import { PlusIcon, PencilIcon, TrashIcon, CheckCircleIcon, XCircleIcon, MagnifyingGlassIcon, XMarkIcon } from '@heroicons/react/24/outline';
import { useIndexers, useCreateIndexer, useUpdateIndexer, useDeleteIndexer } from '../../api/hooks';
import type { Indexer as ApiIndexer } from '../../types';

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
  baseUrl: string;
  apiKey: string;
  categories?: number[];
  minimumSeeders?: number;
  seedRatio?: number;
  seedTime?: number;
  earlyReleaseLimit?: number;
}

type IndexerTemplate = {
  name: string;
  implementation: string;
  protocol: 'usenet' | 'torrent';
  description: string;
  fields: string[];
};

const indexerTemplates: IndexerTemplate[] = [
  {
    name: 'Newznab',
    implementation: 'Newznab',
    protocol: 'usenet',
    description: 'Generic Newznab indexer',
    fields: ['baseUrl', 'apiKey', 'categories']
  },
  {
    name: 'Torznab',
    implementation: 'Torznab',
    protocol: 'torrent',
    description: 'Generic Torznab indexer (Jackett/Prowlarr)',
    fields: ['baseUrl', 'apiKey', 'categories', 'minimumSeeders', 'seedRatio', 'seedTime']
  },
  {
    name: 'Nyaa',
    implementation: 'Nyaa',
    protocol: 'torrent',
    description: 'Nyaa.si anime torrent site',
    fields: ['baseUrl', 'categories', 'minimumSeeders']
  },
  {
    name: 'TorrentLeech',
    implementation: 'TorrentLeech',
    protocol: 'torrent',
    description: 'Private torrent tracker',
    fields: ['baseUrl', 'apiKey', 'categories', 'minimumSeeders', 'seedRatio', 'seedTime']
  },
  {
    name: 'IPTorrents',
    implementation: 'IPTorrents',
    protocol: 'torrent',
    description: 'Private general tracker',
    fields: ['baseUrl', 'apiKey', 'categories', 'minimumSeeders', 'seedRatio', 'seedTime']
  },
  {
    name: 'FileList',
    implementation: 'FileList',
    protocol: 'torrent',
    description: 'Romanian private tracker',
    fields: ['baseUrl', 'apiKey', 'categories', 'minimumSeeders', 'seedRatio', 'seedTime']
  }
];

export default function IndexersSettings({ showAdvanced }: IndexersSettingsProps) {
  // Fetch indexers from API (auto-refreshes every 30 seconds to show Prowlarr-synced indexers)
  const { data: apiIndexers = [], isLoading } = useIndexers();

  // Mutations for creating, updating, and deleting indexers
  const createIndexer = useCreateIndexer();
  const updateIndexer = useUpdateIndexer();
  const deleteIndexer = useDeleteIndexer();

  // Transform API response to component format
  const indexers = useMemo(() => {
    return apiIndexers.map(indexer => {
      const getField = (name: string) => indexer.fields.find(f => f.name === name)?.value;
      const baseUrl = getField('baseUrl') as string || '';
      const apiKey = getField('apiKey') as string || '';
      const categories = getField('categories') as string || '';
      const minimumSeeders = getField('minimumSeeders') as string || '1';
      const seedRatio = getField('seedRatio') as string;
      const seedTime = getField('seedTime') as string;
      const earlyReleaseLimit = getField('earlyReleaseLimit') as string;

      return {
        id: indexer.id,
        name: indexer.name,
        implementation: indexer.implementation,
        protocol: (indexer.implementation === 'Torznab' ? 'torrent' : 'usenet') as 'usenet' | 'torrent',
        enabled: indexer.enable,
        priority: indexer.priority,
        baseUrl,
        apiKey,
        categories: categories ? categories.split(',').map(c => parseInt(c.trim(), 10)) : [],
        minimumSeeders: parseInt(minimumSeeders, 10),
        seedRatio: seedRatio ? parseFloat(seedRatio) : undefined,
        seedTime: seedTime ? parseInt(seedTime, 10) : undefined,
        earlyReleaseLimit: earlyReleaseLimit ? parseInt(earlyReleaseLimit, 10) : undefined
      };
    });
  }, [apiIndexers]);

  const [showAddModal, setShowAddModal] = useState(false);
  const [editingIndexer, setEditingIndexer] = useState<Indexer | null>(null);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState<number | null>(null);
  const [selectedTemplate, setSelectedTemplate] = useState<IndexerTemplate | null>(null);
  const [error, setError] = useState<string | null>(null);

  // Form state
  const [formData, setFormData] = useState<Partial<Indexer>>({
    enabled: true,
    priority: 25,
    categories: [],
    minimumSeeders: 1,
    seedRatio: 1.0,
    seedTime: 0
  });

  const handleSelectTemplate = (template: IndexerTemplate) => {
    setSelectedTemplate(template);
    setFormData({
      name: template.name,
      implementation: template.implementation,
      protocol: template.protocol,
      enabled: true,
      priority: 25,
      baseUrl: '',
      apiKey: '',
      categories: [],
      minimumSeeders: template.protocol === 'torrent' ? 1 : undefined,
      seedRatio: template.protocol === 'torrent' ? 1.0 : undefined,
      seedTime: template.protocol === 'torrent' ? 0 : undefined
    });
  };

  const handleFormChange = (field: keyof Indexer, value: any) => {
    setFormData(prev => ({ ...prev, [field]: value }));
  };

  // Helper function to convert component format to API format
  const toApiFormat = (indexer: Partial<Indexer>): Partial<ApiIndexer> => {
    const fields: { name: string; value: string | string[] }[] = [
      { name: 'baseUrl', value: indexer.baseUrl || '' },
      { name: 'apiKey', value: indexer.apiKey || '' },
      { name: 'categories', value: indexer.categories?.join(',') || '' },
      { name: 'minimumSeeders', value: String(indexer.minimumSeeders || 1) },
    ];

    if (indexer.seedRatio !== undefined) {
      fields.push({ name: 'seedRatio', value: String(indexer.seedRatio) });
    }
    if (indexer.seedTime !== undefined) {
      fields.push({ name: 'seedTime', value: String(indexer.seedTime) });
    }
    if (indexer.earlyReleaseLimit !== undefined) {
      fields.push({ name: 'earlyReleaseLimit', value: String(indexer.earlyReleaseLimit) });
    }

    return {
      id: indexer.id,
      name: indexer.name || '',
      implementation: indexer.implementation || 'Torznab',
      enable: indexer.enabled ?? true,
      priority: indexer.priority || 25,
      fields,
    };
  };

  const handleSaveIndexer = async () => {
    try {
      setError(null);
      const apiIndexer = toApiFormat(formData);

      if (editingIndexer) {
        // Update existing indexer
        await updateIndexer.mutateAsync(apiIndexer as ApiIndexer);
      } else {
        // Create new indexer
        await createIndexer.mutateAsync(apiIndexer as Omit<ApiIndexer, 'id'>);
      }

      // Reset form
      setShowAddModal(false);
      setSelectedTemplate(null);
      setEditingIndexer(null);
      setFormData({
        enabled: true,
        priority: 25,
        categories: [],
        minimumSeeders: 1,
        seedRatio: 1.0,
        seedTime: 0
      });
    } catch (err) {
      console.error('Error saving indexer:', err);
      setError(err instanceof Error ? err.message : 'Failed to save indexer');
    }
  };

  const handleEditIndexer = (indexer: Indexer) => {
    setEditingIndexer(indexer);
    setFormData(indexer);
    const template = indexerTemplates.find(t => t.implementation === indexer.implementation);
    setSelectedTemplate(template || null);
  };

  const handleDeleteIndexer = async (id: number) => {
    try {
      setError(null);
      await deleteIndexer.mutateAsync(id);
      setShowDeleteConfirm(null);
    } catch (err) {
      console.error('Error deleting indexer:', err);
      setError(err instanceof Error ? err.message : 'Failed to delete indexer');
      setShowDeleteConfirm(null);
    }
  };

  const handleTestIndexer = (indexer: Indexer | Partial<Indexer>) => {
    console.log(`Testing connection to ${indexer.name || 'Indexer'}...`);
  };

  const handleCancelEdit = () => {
    setShowAddModal(false);
    setEditingIndexer(null);
    setSelectedTemplate(null);
    setFormData({
      enabled: true,
      priority: 25,
      categories: [],
      minimumSeeders: 1,
      seedRatio: 1.0,
      seedTime: 0
    });
  };

  const renderConfigurationForm = () => {
    if (!selectedTemplate && !editingIndexer) return null;

    const template = selectedTemplate;
    const isTorrent = formData.protocol === 'torrent';
    const isUsenet = formData.protocol === 'usenet';
    const hasField = (field: string) => template?.fields.includes(field) || false;

    return (
      <div className="space-y-6">
        {/* Basic Settings */}
        <div className="space-y-4">
          <h4 className="text-lg font-semibold text-white">Basic Settings</h4>

          <div>
            <label className="block text-sm font-medium text-gray-300 mb-2">Name *</label>
            <input
              type="text"
              value={formData.name || ''}
              onChange={(e) => handleFormChange('name', e.target.value)}
              className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
              placeholder="My Indexer"
            />
          </div>

          <label className="flex items-center space-x-3 cursor-pointer">
            <input
              type="checkbox"
              checked={formData.enabled || false}
              onChange={(e) => handleFormChange('enabled', e.target.checked)}
              className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
            />
            <span className="text-sm font-medium text-gray-300">Enable this indexer</span>
          </label>
        </div>

        {/* Connection */}
        {hasField('baseUrl') && (
          <div className="space-y-4">
            <h4 className="text-lg font-semibold text-white">Connection</h4>

            <div>
              <label className="block text-sm font-medium text-gray-300 mb-2">URL *</label>
              <input
                type="text"
                value={formData.baseUrl || ''}
                onChange={(e) => handleFormChange('baseUrl', e.target.value)}
                className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                placeholder={isUsenet ? 'https://indexer.com' : 'http://localhost:9117/api/v2.0/indexers/torrentleech/'}
              />
              <p className="text-xs text-gray-500 mt-1">
                {isUsenet ? 'Newznab feed URL' : 'Torznab feed URL (from Jackett/Prowlarr)'}
              </p>
            </div>
          </div>
        )}

        {/* Authentication */}
        {hasField('apiKey') && (
          <div className="space-y-4">
            <h4 className="text-lg font-semibold text-white">Authentication</h4>

            <div>
              <label className="block text-sm font-medium text-gray-300 mb-2">API Key *</label>
              <input
                type="password"
                value={formData.apiKey || ''}
                onChange={(e) => handleFormChange('apiKey', e.target.value)}
                className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                placeholder="Enter your API key"
              />
              <p className="text-xs text-gray-500 mt-1">
                API key from your {formData.implementation} account
              </p>
            </div>
          </div>
        )}

        {/* Categories */}
        {hasField('categories') && (
          <div className="space-y-4">
            <h4 className="text-lg font-semibold text-white">Categories</h4>

            <div>
              <label className="block text-sm font-medium text-gray-300 mb-2">Category IDs</label>
              <input
                type="text"
                value={(formData.categories || []).join(', ')}
                onChange={(e) => {
                  const cats = e.target.value.split(',').map(c => parseInt(c.trim())).filter(c => !isNaN(c));
                  handleFormChange('categories', cats);
                }}
                className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                placeholder="5000, 5030, 5040 (combat sports categories)"
              />
              <p className="text-xs text-gray-500 mt-1">
                Comma-separated category IDs. Leave empty to search all categories.
              </p>
            </div>
          </div>
        )}

        {/* Torrent Settings */}
        {isTorrent && (
          <div className="space-y-4">
            <h4 className="text-lg font-semibold text-white">Torrent Settings</h4>

            {hasField('minimumSeeders') && (
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Minimum Seeders</label>
                <input
                  type="number"
                  value={formData.minimumSeeders || 0}
                  onChange={(e) => handleFormChange('minimumSeeders', parseInt(e.target.value))}
                  min="0"
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                />
                <p className="text-xs text-gray-500 mt-1">
                  Minimum number of seeders required to grab a torrent
                </p>
              </div>
            )}

            {showAdvanced && hasField('seedRatio') && (
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Seed Ratio</label>
                <input
                  type="number"
                  step="0.1"
                  value={formData.seedRatio || 0}
                  onChange={(e) => handleFormChange('seedRatio', parseFloat(e.target.value))}
                  min="0"
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                />
                <p className="text-xs text-gray-500 mt-1">
                  Seed ratio required before torrent is stopped. 0 = disabled
                </p>
              </div>
            )}

            {showAdvanced && hasField('seedTime') && (
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Seed Time (minutes)</label>
                <input
                  type="number"
                  value={formData.seedTime || 0}
                  onChange={(e) => handleFormChange('seedTime', parseInt(e.target.value))}
                  min="0"
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                />
                <p className="text-xs text-gray-500 mt-1">
                  Seed time required before torrent is stopped. 0 = disabled
                </p>
              </div>
            )}
          </div>
        )}

        {/* Priority */}
        <div className="space-y-4">
          <h4 className="text-lg font-semibold text-white">Priority</h4>

          <div>
            <label className="block text-sm font-medium text-gray-300 mb-2">Indexer Priority</label>
            <input
              type="number"
              value={formData.priority || 25}
              onChange={(e) => handleFormChange('priority', parseInt(e.target.value))}
              min="1"
              max="50"
              className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
            />
            <p className="text-xs text-gray-500 mt-1">
              Priority when choosing between indexers (1-50, lower is higher priority)
            </p>
          </div>
        </div>

        {/* Advanced Options */}
        {showAdvanced && (
          <div className="space-y-4">
            <h4 className="text-lg font-semibold text-white">Advanced Options</h4>

            <div>
              <label className="block text-sm font-medium text-gray-300 mb-2">Early Release Limit</label>
              <input
                type="number"
                value={formData.earlyReleaseLimit || 0}
                onChange={(e) => handleFormChange('earlyReleaseLimit', parseInt(e.target.value))}
                min="0"
                className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
              />
              <p className="text-xs text-gray-500 mt-1">
                How many days before an event can be grabbed. 0 = disabled
              </p>
            </div>
          </div>
        )}
      </div>
    );
  };

  const isFormValid = () => {
    return formData.name && formData.baseUrl && (formData.protocol === 'usenet' ? formData.apiKey : true);
  };

  return (
    <div className="max-w-6xl mx-auto">
      <div className="mb-8">
        <h2 className="text-3xl font-bold text-white mb-2">Indexers</h2>
        <p className="text-gray-400">
          Configure Usenet indexers and torrent trackers for searching combat sports events
        </p>
      </div>

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
                  <button
                    onClick={() => handleTestIndexer(indexer)}
                    className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                    title="Test"
                  >
                    <CheckCircleIcon className="w-5 h-5" />
                  </button>
                  <button
                    onClick={() => setEditingIndexer(indexer)}
                    className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                    title="Edit"
                  >
                    <PencilIcon className="w-5 h-5" />
                  </button>
                  <button
                    onClick={() => setShowDeleteConfirm(indexer.id)}
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
            <p className="text-gray-500">Loading indexers...</p>
          </div>
        )}

        {!isLoading && indexers.length === 0 && (
          <div className="text-center py-12">
            <MagnifyingGlassIcon className="w-16 h-16 text-gray-700 mx-auto mb-4" />
            <p className="text-gray-500 mb-2">No indexers configured</p>
            <p className="text-sm text-gray-400 mb-4">
              Add indexers to search for combat sports events or sync from Prowlarr
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

      {/* Add/Edit Indexer Modal */}
      {(showAddModal || editingIndexer) && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4 overflow-y-auto">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-4xl w-full my-8">
            <div className="flex items-center justify-between mb-6">
              <h3 className="text-2xl font-bold text-white">
                {editingIndexer ? `Edit ${editingIndexer.name}` : 'Add Indexer'}
              </h3>
              <button
                onClick={handleCancelEdit}
                className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
              >
                <XMarkIcon className="w-6 h-6" />
              </button>
            </div>

            {!selectedTemplate && !editingIndexer ? (
              <>
                <p className="text-gray-400 mb-6">Select an indexer type to configure</p>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-3 max-h-96 overflow-y-auto">
                  {indexerTemplates.map((template) => (
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
                <div className="max-h-[60vh] overflow-y-auto pr-2">
                  {renderConfigurationForm()}
                </div>

                <div className="mt-6 pt-6 border-t border-gray-800 flex items-center justify-end space-x-3">
                  <button
                    onClick={handleCancelEdit}
                    className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
                  >
                    Cancel
                  </button>
                  <button
                    onClick={() => handleTestIndexer(formData)}
                    className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg transition-colors"
                  >
                    Test
                  </button>
                  <button
                    onClick={handleSaveIndexer}
                    disabled={!isFormValid()}
                    className={`px-4 py-2 rounded-lg transition-colors ${
                      isFormValid()
                        ? 'bg-red-600 hover:bg-red-700 text-white'
                        : 'bg-gray-700 text-gray-500 cursor-not-allowed'
                    }`}
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
            <h3 className="text-2xl font-bold text-white mb-4">Delete Indexer?</h3>
            <p className="text-gray-400 mb-6">
              Are you sure you want to delete this indexer? This action cannot be undone.
            </p>
            <div className="flex items-center justify-end space-x-3">
              <button
                onClick={() => setShowDeleteConfirm(null)}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={() => handleDeleteIndexer(showDeleteConfirm)}
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
