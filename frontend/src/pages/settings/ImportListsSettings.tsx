import { useState, useEffect } from 'react';
import { PlusIcon, PencilIcon, TrashIcon, XMarkIcon, ArrowPathIcon } from '@heroicons/react/24/outline';

interface ImportListsSettingsProps {
  showAdvanced: boolean;
}

interface ImportList {
  id?: number;
  name: string;
  enabled: boolean;
  listType: number;
  url: string;
  apiKey?: string;
  qualityProfileId: number;
  rootFolderPath: string;
  monitorEvents: boolean;
  searchOnAdd: boolean;
  tags: number[];
  minimumDaysBeforeEvent: number;
  organizationFilter?: string;
  lastSync?: string;
  lastSyncMessage?: string;
}

interface QualityProfile {
  id: number;
  name: string;
}

interface RootFolder {
  id: number;
  path: string;
}

interface Tag {
  id: number;
  label: string;
}

const IMPORT_LIST_TYPES = [
  { value: 0, label: 'RSS Feed', description: 'Generic RSS feed with event listings' },
  { value: 1, label: 'Custom API', description: 'Tapology, Sherdog, or custom API endpoint' },
  { value: 2, label: 'Calendar/iCal', description: 'iCalendar feed (UFC, Bellator schedules)' },
  { value: 3, label: 'UFC Schedule', description: 'Official UFC event schedule' },
  { value: 4, label: 'Bellator Schedule', description: 'Official Bellator event schedule' },
  { value: 5, label: 'Custom Script', description: 'Custom script/webhook for event discovery' },
];

export default function ImportListsSettings({ showAdvanced }: ImportListsSettingsProps) {
  const [importLists, setImportLists] = useState<ImportList[]>([]);
  const [qualityProfiles, setQualityProfiles] = useState<QualityProfile[]>([]);
  const [rootFolders, setRootFolders] = useState<RootFolder[]>([]);
  const [tags, setTags] = useState<Tag[]>([]);
  const [loading, setLoading] = useState(true);
  const [editingList, setEditingList] = useState<ImportList | null>(null);
  const [showAddModal, setShowAddModal] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState<number | null>(null);
  const [syncingId, setSyncingId] = useState<number | null>(null);

  const [formData, setFormData] = useState<ImportList>({
    name: '',
    enabled: true,
    listType: 0,
    url: '',
    qualityProfileId: 1,
    rootFolderPath: '',
    monitorEvents: true,
    searchOnAdd: false,
    tags: [],
    minimumDaysBeforeEvent: 0,
  });

  useEffect(() => {
    loadData();
  }, []);

  const loadData = async () => {
    try {
      const [lists, profiles, folders, tagsData] = await Promise.all([
        fetch('/api/importlist').then(r => r.ok ? r.json() : []),
        fetch('/api/qualityprofile').then(r => r.ok ? r.json() : []),
        fetch('/api/rootfolder').then(r => r.ok ? r.json() : []),
        fetch('/api/tag').then(r => r.ok ? r.json() : []),
      ]);
      setImportLists(lists);
      setQualityProfiles(profiles);
      setRootFolders(folders);
      setTags(tagsData);
    } catch (error) {
      console.error('Failed to load data:', error);
    } finally {
      setLoading(false);
    }
  };

  const handleSave = async () => {
    try {
      const url = editingList ? `/api/importlist/${editingList.id}` : '/api/importlist';
      const method = editingList ? 'PUT' : 'POST';

      const response = await fetch(url, {
        method,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(formData),
      });

      if (response.ok) {
        await loadData();
        setShowAddModal(false);
        setEditingList(null);
        resetForm();
      }
    } catch (error) {
      console.error('Error saving import list:', error);
    }
  };

  const handleDelete = async (id: number) => {
    try {
      const response = await fetch(`/api/importlist/${id}`, { method: 'DELETE' });
      if (response.ok) {
        await loadData();
        setShowDeleteConfirm(null);
      }
    } catch (error) {
      console.error('Error deleting import list:', error);
    }
  };

  const handleSync = async (id: number) => {
    setSyncingId(id);
    try {
      const response = await fetch(`/api/importlist/${id}/sync`, { method: 'POST' });
      if (response.ok) {
        await loadData();
      }
    } catch (error) {
      console.error('Error syncing import list:', error);
    } finally {
      setSyncingId(null);
    }
  };

  const openAddModal = () => {
    setEditingList(null);
    resetForm();
    setShowAddModal(true);
  };

  const openEditModal = (list: ImportList) => {
    setEditingList(list);
    setFormData({ ...list });
    setShowAddModal(true);
  };

  const resetForm = () => {
    setFormData({
      name: '',
      enabled: true,
      listType: 0,
      url: '',
      qualityProfileId: qualityProfiles[0]?.id || 1,
      rootFolderPath: rootFolders[0]?.path || '',
      monitorEvents: true,
      searchOnAdd: false,
      tags: [],
      minimumDaysBeforeEvent: 0,
    });
  };

  const toggleTag = (tagId: number) => {
    setFormData(prev => ({
      ...prev,
      tags: prev.tags.includes(tagId)
        ? prev.tags.filter(t => t !== tagId)
        : [...prev.tags, tagId]
    }));
  };

  const getListTypeName = (type: number) => {
    return IMPORT_LIST_TYPES.find(t => t.value === type)?.label || 'Unknown';
  };

  if (loading) {
    return (
      <div className="max-w-6xl mx-auto text-center py-12">
        <div className="text-gray-400">Loading import lists...</div>
      </div>
    );
  }

  return (
    <div className="max-w-6xl mx-auto">
      <div className="mb-8">
        <h2 className="text-3xl font-bold text-white mb-2">Import Lists</h2>
        <p className="text-gray-400">
          Automatically discover and add events from external sources
        </p>
      </div>

      {/* Info Box */}
      <div className="mb-8 bg-gradient-to-br from-purple-950/30 to-purple-900/20 border border-purple-900/50 rounded-lg p-6">
        <h3 className="text-lg font-semibold text-white mb-2">Recommended Sources for Combat Sports</h3>
        <ul className="text-sm text-gray-300 space-y-2">
          <li className="flex items-start">
            <span className="text-purple-400 mr-2">•</span>
            <span><strong>TheSportsDB:</strong> Good coverage of MMA/Boxing events with API support</span>
          </li>
          <li className="flex items-start">
            <span className="text-purple-400 mr-2">•</span>
            <span><strong>Tapology:</strong> Excellent for MMA (UFC, Bellator, ONE, PFL) - use Custom API</span>
          </li>
          <li className="flex items-start">
            <span className="text-purple-400 mr-2">•</span>
            <span><strong>RSS Feeds:</strong> Many combat sports sites publish RSS feeds with upcoming events</span>
          </li>
          <li className="flex items-start">
            <span className="text-purple-400 mr-2">•</span>
            <span><strong>iCal/Calendar:</strong> Official promotion schedules (UFC, Bellator publish these)</span>
          </li>
        </ul>
      </div>

      {/* Import Lists Table */}
      <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <div className="flex items-center justify-between mb-6">
          <h3 className="text-xl font-semibold text-white">Import Lists</h3>
          <button
            onClick={openAddModal}
            className="flex items-center px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
          >
            <PlusIcon className="w-4 h-4 mr-2" />
            Add Import List
          </button>
        </div>

        {importLists.length === 0 ? (
          <div className="text-center py-12">
            <p className="text-gray-500 mb-4">No import lists configured</p>
            <p className="text-sm text-gray-600">
              Add an import list to automatically discover events from external sources
            </p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead>
                <tr className="border-b border-gray-800">
                  <th className="text-left py-3 px-4 text-gray-400 font-medium">Name</th>
                  <th className="text-left py-3 px-4 text-gray-400 font-medium">Type</th>
                  <th className="text-left py-3 px-4 text-gray-400 font-medium">Status</th>
                  <th className="text-left py-3 px-4 text-gray-400 font-medium">Last Sync</th>
                  <th className="text-right py-3 px-4 text-gray-400 font-medium">Actions</th>
                </tr>
              </thead>
              <tbody>
                {importLists.map((list) => (
                  <tr key={list.id} className="border-b border-gray-800 hover:bg-gray-900/50 transition-colors">
                    <td className="py-3 px-4">
                      <div className="flex items-center">
                        <span className="text-white font-medium">{list.name}</span>
                        {!list.enabled && (
                          <span className="ml-2 px-2 py-0.5 bg-gray-700 text-gray-400 text-xs rounded">
                            Disabled
                          </span>
                        )}
                      </div>
                    </td>
                    <td className="py-3 px-4 text-gray-300">{getListTypeName(list.listType)}</td>
                    <td className="py-3 px-4">
                      {list.enabled ? (
                        <span className="px-2 py-1 bg-green-900/30 text-green-400 text-xs rounded">
                          Enabled
                        </span>
                      ) : (
                        <span className="px-2 py-1 bg-gray-700 text-gray-400 text-xs rounded">
                          Disabled
                        </span>
                      )}
                    </td>
                    <td className="py-3 px-4 text-sm text-gray-400">
                      {list.lastSync ? new Date(list.lastSync).toLocaleDateString() : 'Never'}
                    </td>
                    <td className="py-3 px-4">
                      <div className="flex items-center justify-end space-x-2">
                        <button
                          onClick={() => handleSync(list.id!)}
                          disabled={syncingId === list.id}
                          className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors disabled:opacity-50"
                          title="Sync Now"
                        >
                          <ArrowPathIcon className={`w-5 h-5 ${syncingId === list.id ? 'animate-spin' : ''}`} />
                        </button>
                        <button
                          onClick={() => openEditModal(list)}
                          className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                          title="Edit"
                        >
                          <PencilIcon className="w-5 h-5" />
                        </button>
                        <button
                          onClick={() => setShowDeleteConfirm(list.id!)}
                          className="p-2 text-gray-400 hover:text-red-400 hover:bg-red-950/30 rounded transition-colors"
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
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4 overflow-y-auto">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-2xl w-full my-8">
            <div className="flex items-center justify-between mb-6">
              <h3 className="text-2xl font-bold text-white">
                {editingList ? 'Edit Import List' : 'Add Import List'}
              </h3>
              <button
                onClick={() => {
                  setShowAddModal(false);
                  setEditingList(null);
                }}
                className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
              >
                <XMarkIcon className="w-6 h-6" />
              </button>
            </div>

            <div className="space-y-6 max-h-[70vh] overflow-y-auto pr-2">
              {/* Name */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Name *</label>
                <input
                  type="text"
                  value={formData.name}
                  onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                  placeholder="UFC Events from Tapology"
                />
              </div>

              {/* List Type */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">List Type *</label>
                <select
                  value={formData.listType}
                  onChange={(e) => setFormData({ ...formData, listType: parseInt(e.target.value) })}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                >
                  {IMPORT_LIST_TYPES.map(type => (
                    <option key={type.value} value={type.value}>
                      {type.label} - {type.description}
                    </option>
                  ))}
                </select>
              </div>

              {/* URL */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">URL *</label>
                <input
                  type="text"
                  value={formData.url}
                  onChange={(e) => setFormData({ ...formData, url: e.target.value })}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600 font-mono text-sm"
                  placeholder="https://example.com/events.rss"
                />
              </div>

              {/* API Key (optional) */}
              {showAdvanced && (
                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-2">API Key (Optional)</label>
                  <input
                    type="password"
                    value={formData.apiKey || ''}
                    onChange={(e) => setFormData({ ...formData, apiKey: e.target.value })}
                    className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600 font-mono text-sm"
                    placeholder="API key if required"
                  />
                </div>
              )}

              {/* Quality Profile */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Quality Profile *</label>
                <select
                  value={formData.qualityProfileId}
                  onChange={(e) => setFormData({ ...formData, qualityProfileId: parseInt(e.target.value) })}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                >
                  {qualityProfiles.map(profile => (
                    <option key={profile.id} value={profile.id}>{profile.name}</option>
                  ))}
                </select>
              </div>

              {/* Root Folder */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Root Folder *</label>
                <select
                  value={formData.rootFolderPath}
                  onChange={(e) => setFormData({ ...formData, rootFolderPath: e.target.value })}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                >
                  {rootFolders.map(folder => (
                    <option key={folder.id} value={folder.path}>{folder.path}</option>
                  ))}
                </select>
              </div>

              {/* Checkboxes */}
              <div className="space-y-3">
                <label className="flex items-center space-x-3 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={formData.enabled}
                    onChange={(e) => setFormData({ ...formData, enabled: e.target.checked })}
                    className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                  />
                  <span className="text-sm font-medium text-gray-300">Enable this import list</span>
                </label>

                <label className="flex items-center space-x-3 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={formData.monitorEvents}
                    onChange={(e) => setFormData({ ...formData, monitorEvents: e.target.checked })}
                    className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                  />
                  <span className="text-sm font-medium text-gray-300">Monitor discovered events</span>
                </label>

                <label className="flex items-center space-x-3 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={formData.searchOnAdd}
                    onChange={(e) => setFormData({ ...formData, searchOnAdd: e.target.checked })}
                    className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                  />
                  <span className="text-sm font-medium text-gray-300">Search for events on add</span>
                </label>
              </div>

              {/* Advanced Options */}
              {showAdvanced && (
                <>
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">
                      Minimum Days Before Event
                    </label>
                    <input
                      type="number"
                      min="0"
                      value={formData.minimumDaysBeforeEvent}
                      onChange={(e) => setFormData({ ...formData, minimumDaysBeforeEvent: parseInt(e.target.value) || 0 })}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <p className="text-xs text-gray-500 mt-1">
                      Only add events at least this many days in the future (0 = all future events)
                    </p>
                  </div>

                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">
                      Organization Filter (Optional)
                    </label>
                    <input
                      type="text"
                      value={formData.organizationFilter || ''}
                      onChange={(e) => setFormData({ ...formData, organizationFilter: e.target.value })}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                      placeholder="UFC, Bellator, ONE"
                    />
                    <p className="text-xs text-gray-500 mt-1">
                      Comma-separated organizations to filter (empty = all)
                    </p>
                  </div>
                </>
              )}

              {/* Tags */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Tags (Optional)</label>
                <div className="flex flex-wrap gap-2">
                  {tags.map((tag) => (
                    <button
                      key={tag.id}
                      onClick={() => toggleTag(tag.id)}
                      className={`px-3 py-1 rounded text-sm transition-colors ${
                        formData.tags.includes(tag.id)
                          ? 'bg-red-600 text-white'
                          : 'bg-gray-800 text-gray-400 hover:bg-gray-700'
                      }`}
                    >
                      {tag.label}
                    </button>
                  ))}
                  {tags.length === 0 && (
                    <p className="text-sm text-gray-500">No tags available</p>
                  )}
                </div>
              </div>
            </div>

            <div className="mt-6 pt-6 border-t border-gray-800 flex items-center justify-end space-x-3">
              <button
                onClick={() => {
                  setShowAddModal(false);
                  setEditingList(null);
                }}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={handleSave}
                disabled={!formData.name || !formData.url}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed text-white rounded-lg transition-colors"
              >
                Save
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Delete Confirmation */}
      {showDeleteConfirm !== null && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-md w-full">
            <h3 className="text-2xl font-bold text-white mb-4">Delete Import List?</h3>
            <p className="text-gray-400 mb-6">
              Are you sure you want to delete this import list? This action cannot be undone.
            </p>
            <div className="flex items-center justify-end space-x-3">
              <button
                onClick={() => setShowDeleteConfirm(null)}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
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
