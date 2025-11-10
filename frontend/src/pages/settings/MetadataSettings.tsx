import { useState, useEffect } from 'react';
import { PlusIcon, PencilIcon, TrashIcon, XMarkIcon, InformationCircleIcon, CheckCircleIcon, XCircleIcon } from '@heroicons/react/24/outline';
import apiClient from '../../api/client';

interface MetadataSettingsProps {
  showAdvanced: boolean;
}

interface MetadataProvider {
  id: number;
  name: string;
  type: number; // Kodi=0, Plex=1, Emby=2, Jellyfin=3, WDTV=4
  enabled: boolean;
  eventNfo: boolean;
  fightCardNfo: boolean;
  eventImages: boolean;
  fighterImages: boolean;
  organizationLogos: boolean;
  eventNfoFilename: string;
  eventPosterFilename: string;
  eventFanartFilename: string;
  useEventFolder: boolean;
  imageQuality: number;
  tags: number[];
  created?: string;
  lastModified?: string;
}

interface Tag {
  id: number;
  label: string;
}

const metadataTypes = [
  { value: 0, label: 'Kodi/XBMC', description: 'NFO and images for Kodi media center' },
  { value: 1, label: 'Plex', description: 'Plex-compatible metadata format' },
  { value: 2, label: 'Emby', description: 'Emby-compatible metadata format' },
  { value: 3, label: 'Jellyfin', description: 'Jellyfin-compatible metadata format' },
  { value: 4, label: 'WDTV', description: 'WDTV media player metadata format' }
];

const defaultProvider: Omit<MetadataProvider, 'id' | 'created' | 'lastModified'> = {
  name: '',
  type: 0,
  enabled: true,
  eventNfo: true,
  fightCardNfo: false,
  eventImages: true,
  fighterImages: false,
  organizationLogos: false,
  eventNfoFilename: '{Event Title}.nfo',
  eventPosterFilename: 'poster.jpg',
  eventFanartFilename: 'fanart.jpg',
  useEventFolder: true,
  imageQuality: 95,
  tags: []
};

export default function MetadataSettings({ showAdvanced }: MetadataSettingsProps) {
  const [providers, setProviders] = useState<MetadataProvider[]>([]);
  const [tags, setTags] = useState<Tag[]>([]);
  const [showAddModal, setShowAddModal] = useState(false);
  const [editingProvider, setEditingProvider] = useState<MetadataProvider | null>(null);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState<number | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [formData, setFormData] = useState<Omit<MetadataProvider, 'id' | 'created' | 'lastModified'>>(defaultProvider);

  useEffect(() => {
    loadProviders();
    loadTags();
  }, []);

  const loadProviders = async () => {
    try {
      setIsLoading(true);
      const response = await apiClient.get('/metadata');
      setProviders(response.data);
    } catch (error) {
      console.error('Failed to load metadata providers:', error);
    } finally {
      setIsLoading(false);
    }
  };

  const loadTags = async () => {
    try {
      const response = await apiClient.get('/tag');
      setTags(response.data);
    } catch (error) {
      console.error('Failed to load tags:', error);
    }
  };

  const handleAdd = () => {
    setFormData({ ...defaultProvider, name: metadataTypes[0].label });
    setEditingProvider(null);
    setShowAddModal(true);
  };

  const handleEdit = (provider: MetadataProvider) => {
    setFormData({
      name: provider.name,
      type: provider.type,
      enabled: provider.enabled,
      eventNfo: provider.eventNfo,
      fightCardNfo: provider.fightCardNfo,
      eventImages: provider.eventImages,
      fighterImages: provider.fighterImages,
      organizationLogos: provider.organizationLogos,
      eventNfoFilename: provider.eventNfoFilename,
      eventPosterFilename: provider.eventPosterFilename,
      eventFanartFilename: provider.eventFanartFilename,
      useEventFolder: provider.useEventFolder,
      imageQuality: provider.imageQuality,
      tags: provider.tags
    });
    setEditingProvider(provider);
    setShowAddModal(true);
  };

  const handleSave = async () => {
    try {
      if (editingProvider) {
        await apiClient.put(`/metadata/${editingProvider.id}`, formData);
      } else {
        await apiClient.post('/metadata', formData);
      }
      setShowAddModal(false);
      loadProviders();
    } catch (error) {
      console.error('Failed to save metadata provider:', error);
    }
  };

  const handleDelete = async (id: number) => {
    try {
      await apiClient.delete(`/metadata/${id}`);
      setShowDeleteConfirm(null);
      loadProviders();
    } catch (error) {
      console.error('Failed to delete metadata provider:', error);
    }
  };

  const handleTypeChange = (type: number) => {
    setFormData({
      ...formData,
      type,
      name: metadataTypes.find(t => t.value === type)?.label || formData.name
    });
  };

  const getTypeLabel = (type: number) => {
    return metadataTypes.find(t => t.value === type)?.label || 'Unknown';
  };

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-3xl font-bold text-white mb-2">Metadata</h2>
        <p className="text-gray-400">Configure metadata providers for media servers (Kodi, Plex, Emby, Jellyfin)</p>
      </div>

      {/* Info Box */}
      <div className="bg-blue-900/20 border border-blue-700/50 rounded-lg p-4">
        <div className="flex items-start">
          <InformationCircleIcon className="w-5 h-5 text-blue-400 mr-3 mt-0.5 flex-shrink-0" />
          <div className="text-sm text-blue-200">
            <p className="mb-2">
              Metadata providers generate NFO files and download images for your combat sports events.
              These files allow media servers like Kodi, Plex, Emby, and Jellyfin to display rich information
              about events, fighters, organizations, and fight cards.
            </p>
            <p>
              <strong>NFO files</strong> contain event details, fighters, results, and organization info.{' '}
              <strong>Images</strong> include event posters, fighter photos, and organization logos.
            </p>
          </div>
        </div>
      </div>

      {/* Metadata Providers List */}
      <div className="bg-gradient-to-br from-gray-900 to-black border border-gray-700 rounded-lg overflow-hidden">
        <div className="flex items-center justify-between p-6 border-b border-gray-700">
          <h3 className="text-xl font-semibold text-white">Metadata Providers</h3>
          <button
            onClick={handleAdd}
            className="flex items-center px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
          >
            <PlusIcon className="w-5 h-5 mr-2" />
            Add Provider
          </button>
        </div>

        {isLoading ? (
          <div className="p-8 text-center text-gray-400">Loading metadata providers...</div>
        ) : providers.length === 0 ? (
          <div className="p-8 text-center text-gray-400">
            No metadata providers configured. Click "Add Provider" to create one.
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead>
                <tr className="bg-gray-800 text-gray-300 text-sm">
                  <th className="px-6 py-3 text-left font-medium">Name</th>
                  <th className="px-6 py-3 text-left font-medium">Type</th>
                  <th className="px-6 py-3 text-center font-medium">NFO Files</th>
                  <th className="px-6 py-3 text-center font-medium">Images</th>
                  <th className="px-6 py-3 text-center font-medium">Status</th>
                  <th className="px-6 py-3 text-right font-medium">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-700">
                {providers.map((provider) => (
                  <tr key={provider.id} className="hover:bg-gray-800/50 transition-colors">
                    <td className="px-6 py-4">
                      <div className="text-white font-medium">{provider.name}</div>
                    </td>
                    <td className="px-6 py-4">
                      <div className="text-gray-300">{getTypeLabel(provider.type)}</div>
                    </td>
                    <td className="px-6 py-4 text-center">
                      <div className="flex items-center justify-center gap-2">
                        {provider.eventNfo && (
                          <span className="px-2 py-1 bg-blue-900/30 text-blue-400 text-xs rounded">Event</span>
                        )}
                        {provider.fightCardNfo && (
                          <span className="px-2 py-1 bg-purple-900/30 text-purple-400 text-xs rounded">Fights</span>
                        )}
                        {!provider.eventNfo && !provider.fightCardNfo && (
                          <span className="text-gray-500 text-xs">None</span>
                        )}
                      </div>
                    </td>
                    <td className="px-6 py-4 text-center">
                      <div className="flex items-center justify-center gap-2">
                        {provider.eventImages && (
                          <span className="px-2 py-1 bg-green-900/30 text-green-400 text-xs rounded">Events</span>
                        )}
                        {provider.fighterImages && (
                          <span className="px-2 py-1 bg-yellow-900/30 text-yellow-400 text-xs rounded">Fighters</span>
                        )}
                        {provider.organizationLogos && (
                          <span className="px-2 py-1 bg-orange-900/30 text-orange-400 text-xs rounded">Logos</span>
                        )}
                        {!provider.eventImages && !provider.fighterImages && !provider.organizationLogos && (
                          <span className="text-gray-500 text-xs">None</span>
                        )}
                      </div>
                    </td>
                    <td className="px-6 py-4 text-center">
                      {provider.enabled ? (
                        <div className="flex items-center justify-center text-green-400">
                          <CheckCircleIcon className="w-5 h-5" />
                        </div>
                      ) : (
                        <div className="flex items-center justify-center text-gray-500">
                          <XCircleIcon className="w-5 h-5" />
                        </div>
                      )}
                    </td>
                    <td className="px-6 py-4">
                      <div className="flex items-center justify-end gap-2">
                        <button
                          onClick={() => handleEdit(provider)}
                          className="p-2 text-blue-400 hover:text-blue-300 hover:bg-blue-900/30 rounded transition-colors"
                          title="Edit"
                        >
                          <PencilIcon className="w-5 h-5" />
                        </button>
                        <button
                          onClick={() => setShowDeleteConfirm(provider.id)}
                          className="p-2 text-red-400 hover:text-red-300 hover:bg-red-900/30 rounded transition-colors"
                          title="Delete"
                        >
                          <TrashIcon className="w-5 h-5" />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Add/Edit Modal */}
      {showAddModal && (
        <div className="fixed inset-0 bg-black bg-opacity-75 flex items-center justify-center z-50 p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-gray-700 rounded-lg w-full max-w-3xl max-h-[90vh] overflow-y-auto">
            <div className="sticky top-0 bg-gray-900 border-b border-gray-700 p-6 flex items-center justify-between z-10">
              <h3 className="text-2xl font-bold text-white">
                {editingProvider ? 'Edit Metadata Provider' : 'Add Metadata Provider'}
              </h3>
              <button
                onClick={() => setShowAddModal(false)}
                className="text-gray-400 hover:text-white transition-colors"
              >
                <XMarkIcon className="w-6 h-6" />
              </button>
            </div>

            <div className="p-6 space-y-6">
              {/* Basic Settings */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Provider Type</label>
                <select
                  value={formData.type}
                  onChange={(e) => handleTypeChange(Number(e.target.value))}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-600 rounded-lg text-white focus:outline-none focus:border-red-600"
                >
                  {metadataTypes.map(type => (
                    <option key={type.value} value={type.value}>
                      {type.label} - {type.description}
                    </option>
                  ))}
                </select>
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Name</label>
                <input
                  type="text"
                  value={formData.name}
                  onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-600 rounded-lg text-white focus:outline-none focus:border-red-600"
                  placeholder="e.g., Kodi Metadata"
                />
              </div>

              <div className="flex items-center">
                <input
                  type="checkbox"
                  id="enabled"
                  checked={formData.enabled}
                  onChange={(e) => setFormData({ ...formData, enabled: e.target.checked })}
                  className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600 focus:ring-offset-gray-900"
                />
                <label htmlFor="enabled" className="ml-3 text-sm text-gray-300">
                  Enable this metadata provider
                </label>
              </div>

              {/* NFO Settings */}
              <div className="border-t border-gray-700 pt-6">
                <h4 className="text-lg font-semibold text-white mb-4">NFO Files</h4>
                <div className="space-y-3">
                  <div className="flex items-center">
                    <input
                      type="checkbox"
                      id="eventNfo"
                      checked={formData.eventNfo}
                      onChange={(e) => setFormData({ ...formData, eventNfo: e.target.checked })}
                      className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600 focus:ring-offset-gray-900"
                    />
                    <label htmlFor="eventNfo" className="ml-3 text-sm text-gray-300">
                      Generate event NFO files (event details, fighters, organization)
                    </label>
                  </div>

                  <div className="flex items-center">
                    <input
                      type="checkbox"
                      id="fightCardNfo"
                      checked={formData.fightCardNfo}
                      onChange={(e) => setFormData({ ...formData, fightCardNfo: e.target.checked })}
                      className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600 focus:ring-offset-gray-900"
                    />
                    <label htmlFor="fightCardNfo" className="ml-3 text-sm text-gray-300">
                      Generate individual fight card NFO files
                    </label>
                  </div>

                  {formData.eventNfo && (
                    <div>
                      <label className="block text-sm font-medium text-gray-300 mb-2">
                        Event NFO Filename Pattern
                      </label>
                      <input
                        type="text"
                        value={formData.eventNfoFilename}
                        onChange={(e) => setFormData({ ...formData, eventNfoFilename: e.target.value })}
                        className="w-full px-4 py-2 bg-gray-800 border border-gray-600 rounded-lg text-white focus:outline-none focus:border-red-600"
                        placeholder="{Event Title}.nfo"
                      />
                      <p className="mt-1 text-xs text-gray-400">
                        Available tokens: {'{Event Title}'}, {'{League}'}, {'{Event Date}'}
                      </p>
                    </div>
                  )}
                </div>
              </div>

              {/* Image Settings */}
              <div className="border-t border-gray-700 pt-6">
                <h4 className="text-lg font-semibold text-white mb-4">Images</h4>
                <div className="space-y-3">
                  <div className="flex items-center">
                    <input
                      type="checkbox"
                      id="eventImages"
                      checked={formData.eventImages}
                      onChange={(e) => setFormData({ ...formData, eventImages: e.target.checked })}
                      className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600 focus:ring-offset-gray-900"
                    />
                    <label htmlFor="eventImages" className="ml-3 text-sm text-gray-300">
                      Download event posters and fanart
                    </label>
                  </div>

                  <div className="flex items-center">
                    <input
                      type="checkbox"
                      id="fighterImages"
                      checked={formData.fighterImages}
                      onChange={(e) => setFormData({ ...formData, fighterImages: e.target.checked })}
                      className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600 focus:ring-offset-gray-900"
                    />
                    <label htmlFor="fighterImages" className="ml-3 text-sm text-gray-300">
                      Download fighter images
                    </label>
                  </div>

                  <div className="flex items-center">
                    <input
                      type="checkbox"
                      id="organizationLogos"
                      checked={formData.organizationLogos}
                      onChange={(e) => setFormData({ ...formData, organizationLogos: e.target.checked })}
                      className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600 focus:ring-offset-gray-900"
                    />
                    <label htmlFor="organizationLogos" className="ml-3 text-sm text-gray-300">
                      Download organization logos (UFC, Bellator, etc.)
                    </label>
                  </div>

                  {formData.eventImages && (
                    <div className="grid grid-cols-2 gap-4">
                      <div>
                        <label className="block text-sm font-medium text-gray-300 mb-2">
                          Poster Filename
                        </label>
                        <input
                          type="text"
                          value={formData.eventPosterFilename}
                          onChange={(e) => setFormData({ ...formData, eventPosterFilename: e.target.value })}
                          className="w-full px-4 py-2 bg-gray-800 border border-gray-600 rounded-lg text-white focus:outline-none focus:border-red-600"
                          placeholder="poster.jpg"
                        />
                      </div>
                      <div>
                        <label className="block text-sm font-medium text-gray-300 mb-2">
                          Fanart Filename
                        </label>
                        <input
                          type="text"
                          value={formData.eventFanartFilename}
                          onChange={(e) => setFormData({ ...formData, eventFanartFilename: e.target.value })}
                          className="w-full px-4 py-2 bg-gray-800 border border-gray-600 rounded-lg text-white focus:outline-none focus:border-red-600"
                          placeholder="fanart.jpg"
                        />
                      </div>
                    </div>
                  )}
                </div>
              </div>

              {/* Advanced Settings */}
              {showAdvanced && (
                <div className="border-t border-yellow-700 pt-6">
                  <h4 className="text-lg font-semibold text-white mb-4">
                    Advanced Settings
                    <span className="ml-2 px-2 py-0.5 bg-yellow-900/30 text-yellow-400 text-xs rounded">
                      Advanced
                    </span>
                  </h4>
                  <div className="space-y-4">
                    <div className="flex items-center">
                      <input
                        type="checkbox"
                        id="useEventFolder"
                        checked={formData.useEventFolder}
                        onChange={(e) => setFormData({ ...formData, useEventFolder: e.target.checked })}
                        className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600 focus:ring-offset-gray-900"
                      />
                      <label htmlFor="useEventFolder" className="ml-3 text-sm text-gray-300">
                        Store metadata files in event folder (recommended)
                      </label>
                    </div>

                    <div>
                      <label className="block text-sm font-medium text-gray-300 mb-2">
                        Image Quality (JPEG): {formData.imageQuality}%
                      </label>
                      <input
                        type="range"
                        min="50"
                        max="100"
                        value={formData.imageQuality}
                        onChange={(e) => setFormData({ ...formData, imageQuality: Number(e.target.value) })}
                        className="w-full h-2 bg-gray-700 rounded-lg appearance-none cursor-pointer accent-red-600"
                      />
                      <div className="flex justify-between text-xs text-gray-400 mt-1">
                        <span>Smaller files (50%)</span>
                        <span>Best quality (100%)</span>
                      </div>
                    </div>

                    <div>
                      <label className="block text-sm font-medium text-gray-300 mb-2">Tags</label>
                      <select
                        multiple
                        value={formData.tags.map(String)}
                        onChange={(e) => {
                          const selected = Array.from(e.target.selectedOptions, option => Number(option.value));
                          setFormData({ ...formData, tags: selected });
                        }}
                        className="w-full px-4 py-2 bg-gray-800 border border-gray-600 rounded-lg text-white focus:outline-none focus:border-red-600 h-32"
                      >
                        {tags.map(tag => (
                          <option key={tag.id} value={tag.id}>
                            {tag.label}
                          </option>
                        ))}
                      </select>
                      <p className="mt-1 text-xs text-gray-400">
                        Hold Ctrl/Cmd to select multiple tags
                      </p>
                    </div>
                  </div>
                </div>
              )}
            </div>

            <div className="sticky bottom-0 bg-gray-900 border-t border-gray-700 p-6 flex justify-end gap-3">
              <button
                onClick={() => setShowAddModal(false)}
                className="px-6 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={handleSave}
                className="px-6 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
              >
                {editingProvider ? 'Save Changes' : 'Add Provider'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Delete Confirmation Modal */}
      {showDeleteConfirm !== null && (
        <div className="fixed inset-0 bg-black bg-opacity-75 flex items-center justify-center z-50 p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-700 rounded-lg max-w-md w-full p-6">
            <h3 className="text-xl font-bold text-white mb-4">Delete Metadata Provider</h3>
            <p className="text-gray-300 mb-6">
              Are you sure you want to delete this metadata provider? This action cannot be undone.
            </p>
            <div className="flex justify-end gap-3">
              <button
                onClick={() => setShowDeleteConfirm(null)}
                className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={() => handleDelete(showDeleteConfirm)}
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
