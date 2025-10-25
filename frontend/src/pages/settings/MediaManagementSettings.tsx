import { useState, useEffect } from 'react';
import { PlusIcon, FolderIcon, CheckIcon, XMarkIcon } from '@heroicons/react/24/outline';
import { apiGet, apiPost, apiPut, apiDelete } from '../../utils/api';
import FileBrowserModal from '../../components/FileBrowserModal';

interface MediaManagementSettingsProps {
  showAdvanced: boolean;
}

interface RootFolder {
  id: number;
  path: string;
  accessible: boolean;
  freeSpace: number;
}

interface MediaManagementSettingsData {
  renameEvents: boolean;
  replaceIllegalCharacters: boolean;
  standardEventFormat: string;
  prelimsFormat: string;
  createEventFolders: boolean;
  deleteEmptyFolders: boolean;
  skipFreeSpaceCheck: boolean;
  minimumFreeSpace: number;
  useHardlinks: boolean;
  importExtraFiles: boolean;
  extraFileExtensions: string;
  changeFileDate: string;
  recycleBin: string;
  recycleBinCleanup: number;
  setPermissions: boolean;
  chmodFolder: string;
  chownGroup: string;
}

export default function MediaManagementSettings({ showAdvanced }: MediaManagementSettingsProps) {
  const [rootFolders, setRootFolders] = useState<RootFolder[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [showAddFolderModal, setShowAddFolderModal] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState<number | null>(null);
  const [newFolderPath, setNewFolderPath] = useState('');
  const [showFileBrowser, setShowFileBrowser] = useState(false);

  // Media Management Settings stored in database
  const [settings, setSettings] = useState<MediaManagementSettingsData>({
    renameEvents: false,
    replaceIllegalCharacters: true,
    standardEventFormat: '{Event Title} - {Event Date} - {Organization}',
    prelimsFormat: '{Event Title} - Prelims - {Event Date} - {Organization}',
    createEventFolders: true,
    deleteEmptyFolders: false,
    skipFreeSpaceCheck: false,
    minimumFreeSpace: 100,
    useHardlinks: true,
    importExtraFiles: false,
    extraFileExtensions: 'srt,nfo',
    changeFileDate: 'None',
    recycleBin: '',
    recycleBinCleanup: 7,
    setPermissions: false,
    chmodFolder: '755',
    chownGroup: '',
  });

  // Load settings and root folders from API on mount
  useEffect(() => {
    loadSettings();
    fetchRootFolders();
  }, []);

  const loadSettings = async () => {
    try {
      const response = await apiGet('/api/settings');
      if (response.ok) {
        const data = await response.json();
        if (data.mediaManagementSettings) {
          const parsed = JSON.parse(data.mediaManagementSettings);
          setSettings(parsed);
        }
      }
    } catch (error) {
      console.error('Failed to load media management settings:', error);
    }
  };

  const fetchRootFolders = async () => {
    try {
      const response = await apiGet('/api/rootfolder');
      if (response.ok) {
        const data = await response.json();
        setRootFolders(data);
      }
    } catch (error) {
      console.error('Failed to fetch root folders:', error);
    } finally {
      setLoading(false);
    }
  };

  const formatBytes = (bytes: number) => {
    const gb = bytes / (1024 * 1024 * 1024);
    return `${gb.toFixed(2)} GB`;
  };

  const handleAddFolder = async () => {
    if (!newFolderPath.trim()) {
      return;
    }

    try {
      const response = await apiPost('/api/rootfolder', {
        path: newFolderPath.trim(),
      });

      if (response.ok) {
        const newFolder = await response.json();
        setRootFolders(prev => [...prev, newFolder]);
        setShowAddFolderModal(false);
        setNewFolderPath('');
      } else {
        const error = await response.json();
        console.error('Failed to add root folder:', error.error);
      }
    } catch (error) {
      console.error('Failed to add folder:', error);
    }
  };

  const handleDeleteFolder = async (id: number) => {
    try {
      const response = await apiDelete(`/api/rootfolder/${id}`);

      if (response.ok) {
        setRootFolders(prev => prev.filter(f => f.id !== id));
        setShowDeleteConfirm(null);
      }
    } catch (error) {
      console.error('Failed to delete folder:', error);
    }
  };

  const handleSave = async () => {
    setSaving(true);
    try {
      // Get current settings
      const response = await apiGet('/api/settings');
      if (!response.ok) {
        throw new Error('Failed to fetch current settings');
      }
      const currentSettings = await response.json();

      // Update with new media management settings
      const updatedSettings = {
        ...currentSettings,
        mediaManagementSettings: JSON.stringify(settings),
      };

      // Save to API
      const saveResponse = await apiPut('/api/settings', updatedSettings);

      if (!saveResponse.ok) {
        throw new Error('Failed to save settings');
      }
    } catch (error) {
      console.error('Failed to save settings:', error);
    } finally {
      setSaving(false);
    }
  };

  const updateSetting = <K extends keyof MediaManagementSettingsData>(
    key: K,
    value: MediaManagementSettingsData[K]
  ) => {
    setSettings(prev => ({ ...prev, [key]: value }));
  };

  return (
    <div className="max-w-4xl mx-auto">
      <div className="mb-8 flex items-center justify-between">
        <div>
          <h2 className="text-3xl font-bold text-white mb-2">Media Management</h2>
          <p className="text-gray-400">Settings for file naming, root folders, and file management</p>
        </div>
        <button
          onClick={handleSave}
          disabled={saving}
          className="px-6 py-2 bg-red-600 hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed text-white rounded-lg transition-colors"
        >
          {saving ? 'Saving...' : 'Save Changes'}
        </button>
      </div>

      {/* Root Folders */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-xl font-semibold text-white">Root Folders</h3>
          <button
            onClick={() => setShowAddFolderModal(true)}
            className="flex items-center px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
          >
            <PlusIcon className="w-4 h-4 mr-2" />
            Add Root Folder
          </button>
        </div>
        <p className="text-sm text-gray-400 mb-4">
          Root folders where Fightarr will store combat sports events
        </p>

        <div className="space-y-2">
          {rootFolders.map((folder) => (
            <div
              key={folder.id}
              className="flex items-center justify-between p-4 bg-black/30 rounded-lg border border-gray-800"
            >
              <div className="flex items-center flex-1">
                <FolderIcon className="w-5 h-5 text-red-400 mr-3" />
                <div className="flex-1">
                  <p className="text-white font-medium">{folder.path}</p>
                  <p className="text-sm text-gray-400">
                    Free Space: {formatBytes(folder.freeSpace)}
                  </p>
                </div>
              </div>
              <div className="flex items-center space-x-3">
                {folder.accessible ? (
                  <CheckIcon className="w-5 h-5 text-green-500" />
                ) : (
                  <XMarkIcon className="w-5 h-5 text-red-500" />
                )}
                <button
                  onClick={() => setShowDeleteConfirm(folder.id)}
                  className="text-gray-400 hover:text-red-400 text-sm transition-colors"
                >
                  Delete
                </button>
              </div>
            </div>
          ))}
        </div>

        {rootFolders.length === 0 && (
          <div className="text-center py-12">
            <FolderIcon className="w-16 h-16 text-gray-700 mx-auto mb-4" />
            <p className="text-gray-500 mb-2">No root folders configured</p>
            <p className="text-sm text-gray-400">
              Add at least one root folder where Fightarr will store events
            </p>
          </div>
        )}
      </div>

      {/* Event Naming */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <h3 className="text-xl font-semibold text-white mb-4">Event Naming</h3>

        <div className="space-y-4">
          <label className="flex items-start space-x-3 cursor-pointer">
            <input
              type="checkbox"
              checked={settings.renameEvents}
              onChange={(e) => updateSetting('renameEvents', e.target.checked)}
              className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
            />
            <div>
              <span className="text-white font-medium">Rename Events</span>
              <p className="text-sm text-gray-400 mt-1">
                Rename event files based on naming scheme
              </p>
            </div>
          </label>

          <label className="flex items-start space-x-3 cursor-pointer">
            <input
              type="checkbox"
              checked={settings.replaceIllegalCharacters}
              onChange={(e) => updateSetting('replaceIllegalCharacters', e.target.checked)}
              className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
            />
            <div>
              <span className="text-white font-medium">Replace Illegal Characters</span>
              <p className="text-sm text-gray-400 mt-1">
                Replace illegal characters with replacement character
              </p>
            </div>
          </label>

          {settings.renameEvents && (
            <>
              <div>
                <label className="block text-white font-medium mb-2">Standard Event Format</label>
                <div className="relative">
                  <input
                    type="text"
                    value={settings.standardEventFormat}
                    onChange={(e) => updateSetting('standardEventFormat', e.target.value)}
                    className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600 font-mono"
                    placeholder="{Event Title} - {Event Date} - {Organization}"
                  />
                </div>

                {/* Token Helper */}
                <div className="mt-3 p-4 bg-black/30 rounded-lg border border-gray-800">
                  <p className="text-sm font-medium text-gray-300 mb-2">Available Tokens (click to insert):</p>
                  <div className="grid grid-cols-2 md:grid-cols-3 gap-2">
                    {[
                      { token: '{Event Title}', desc: 'UFC 300' },
                      { token: '{Organization}', desc: 'Ultimate Fighting Championship' },
                      { token: '{Event Date}', desc: '2024-04-13' },
                      { token: '{Event Date:yyyy}', desc: '2024' },
                      { token: '{Event Date:MM}', desc: '04' },
                      { token: '{Quality Full}', desc: 'Bluray-1080p' },
                      { token: '{Quality Title}', desc: '1080p' },
                      { token: '{Release Group}', desc: 'GROUP' },
                      { token: '{Preferred Words}', desc: 'REPACK' },
                    ].map((item) => (
                      <button
                        key={item.token}
                        onClick={() => {
                          const input = document.querySelector('input[placeholder*="Event Title"]') as HTMLInputElement;
                          if (input) {
                            const cursorPos = input.selectionStart || settings.standardEventFormat.length;
                            const newValue =
                              settings.standardEventFormat.slice(0, cursorPos) +
                              item.token +
                              settings.standardEventFormat.slice(cursorPos);
                            updateSetting('standardEventFormat', newValue);
                          }
                        }}
                        className="text-left px-3 py-2 bg-gray-800 hover:bg-gray-700 border border-gray-700 hover:border-red-600 rounded text-sm transition-colors group"
                      >
                        <div className="font-mono text-purple-400 text-xs group-hover:text-purple-300">{item.token}</div>
                        <div className="text-gray-500 text-xs mt-0.5">{item.desc}</div>
                      </button>
                    ))}
                  </div>
                </div>

                {/* Live Preview */}
                <div className="mt-3 p-4 bg-gradient-to-r from-blue-950/30 to-purple-950/30 border border-blue-900/50 rounded-lg">
                  <p className="text-sm font-medium text-blue-300 mb-2">Preview:</p>
                  <p className="text-white font-mono text-sm break-all">
                    {settings.standardEventFormat
                      .replace(/{Event Title}/g, 'UFC 300')
                      .replace(/{Organization}/g, 'Ultimate Fighting Championship')
                      .replace(/{Event Date:yyyy}/g, '2024')
                      .replace(/{Event Date:MM}/g, '04')
                      .replace(/{Event Date}/g, '2024-04-13')
                      .replace(/{Quality Full}/g, 'Bluray-1080p')
                      .replace(/{Quality Title}/g, '1080p')
                      .replace(/{Release Group}/g, 'GROUP')
                      .replace(/{Preferred Words}/g, 'REPACK')
                    }.mkv
                  </p>
                  <p className="text-xs text-gray-500 mt-2">
                    This shows how your events will be named with the current format
                  </p>
                </div>
              </div>

              {/* Prelims Format */}
              <div className="mt-6 pt-6 border-t border-gray-800">
                <label className="block text-white font-medium mb-2">
                  Preliminary Card Format
                  <span className="ml-2 text-sm text-gray-400 font-normal">(Optional - for separate prelim files)</span>
                </label>
                <div className="relative">
                  <input
                    type="text"
                    value={settings.prelimsFormat}
                    onChange={(e) => updateSetting('prelimsFormat', e.target.value)}
                    className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600 font-mono"
                    placeholder="{Event Title} - Prelims - {Event Date}"
                  />
                </div>

                {/* Prelims Token Helper */}
                <div className="mt-3 p-4 bg-black/30 rounded-lg border border-gray-800">
                  <p className="text-sm font-medium text-gray-300 mb-2">Available Tokens (click to insert):</p>
                  <div className="grid grid-cols-2 md:grid-cols-3 gap-2">
                    {[
                      { token: '{Event Title}', desc: 'UFC 300' },
                      { token: '{Organization}', desc: 'Ultimate Fighting Championship' },
                      { token: '{Event Date}', desc: '2024-04-13' },
                      { token: '{Event Date:yyyy}', desc: '2024' },
                      { token: '{Card Type}', desc: 'Prelims' },
                      { token: '{Quality Full}', desc: 'Bluray-1080p' },
                      { token: '{Quality Title}', desc: '1080p' },
                      { token: '{Release Group}', desc: 'GROUP' },
                    ].map((item) => (
                      <button
                        key={item.token}
                        onClick={() => {
                          const inputs = document.querySelectorAll('input[placeholder*="Prelims"]') as NodeListOf<HTMLInputElement>;
                          const input = inputs[inputs.length - 1];
                          if (input) {
                            const cursorPos = input.selectionStart || settings.prelimsFormat.length;
                            const newValue =
                              settings.prelimsFormat.slice(0, cursorPos) +
                              item.token +
                              settings.prelimsFormat.slice(cursorPos);
                            updateSetting('prelimsFormat', newValue);
                          }
                        }}
                        className="text-left px-3 py-2 bg-gray-800 hover:bg-gray-700 border border-gray-700 hover:border-red-600 rounded text-sm transition-colors group"
                      >
                        <div className="font-mono text-purple-400 text-xs group-hover:text-purple-300">{item.token}</div>
                        <div className="text-gray-500 text-xs mt-0.5">{item.desc}</div>
                      </button>
                    ))}
                  </div>
                </div>

                {/* Prelims Preview */}
                <div className="mt-3 p-4 bg-gradient-to-r from-orange-950/30 to-yellow-950/30 border border-orange-900/50 rounded-lg">
                  <p className="text-sm font-medium text-orange-300 mb-2">Prelims Preview:</p>
                  <p className="text-white font-mono text-sm break-all">
                    {settings.prelimsFormat
                      .replace(/{Event Title}/g, 'UFC 300')
                      .replace(/{Organization}/g, 'Ultimate Fighting Championship')
                      .replace(/{Event Date:yyyy}/g, '2024')
                      .replace(/{Event Date:MM}/g, '04')
                      .replace(/{Event Date}/g, '2024-04-13')
                      .replace(/{Card Type}/g, 'Prelims')
                      .replace(/{Quality Full}/g, 'Bluray-1080p')
                      .replace(/{Quality Title}/g, '1080p')
                      .replace(/{Release Group}/g, 'GROUP')
                    }.mkv
                  </p>
                  <p className="text-xs text-gray-500 mt-2">
                    This format applies to preliminary card files (early prelims, prelims)
                  </p>
                </div>
              </div>
            </>
          )}
        </div>
      </div>

      {/* Folders */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <h3 className="text-xl font-semibold text-white mb-4">Folders</h3>

        <div className="space-y-4">
          <label className="flex items-start space-x-3 cursor-pointer">
            <input
              type="checkbox"
              checked={settings.createEventFolders}
              onChange={(e) => updateSetting('createEventFolders', e.target.checked)}
              className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
            />
            <div>
              <span className="text-white font-medium">Create Event Folders</span>
              <p className="text-sm text-gray-400 mt-1">
                Create individual folders for each event
              </p>
            </div>
          </label>

          {showAdvanced && (
            <label className="flex items-start space-x-3 cursor-pointer">
              <input
                type="checkbox"
                checked={settings.deleteEmptyFolders}
                onChange={(e) => updateSetting('deleteEmptyFolders', e.target.checked)}
                className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
              />
              <div>
                <span className="text-white font-medium">Delete Empty Folders</span>
                <p className="text-sm text-gray-400 mt-1">
                  Delete empty event folders during disk scan
                </p>
                <span className="inline-block mt-1 px-2 py-0.5 bg-yellow-900/30 text-yellow-400 text-xs rounded">
                  Advanced
                </span>
              </div>
            </label>
          )}
        </div>
      </div>

      {/* Importing */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <h3 className="text-xl font-semibold text-white mb-4">Importing</h3>

        <div className="space-y-4">
          <label className="flex items-start space-x-3 cursor-pointer">
            <input
              type="checkbox"
              checked={settings.useHardlinks}
              onChange={(e) => updateSetting('useHardlinks', e.target.checked)}
              className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
            />
            <div>
              <span className="text-white font-medium">Use Hardlinks instead of Copy</span>
              <p className="text-sm text-gray-400 mt-1">
                Use hardlinks when copying files from torrents (requires same filesystem)
              </p>
            </div>
          </label>

          <label className="flex items-start space-x-3 cursor-pointer">
            <input
              type="checkbox"
              checked={settings.importExtraFiles}
              onChange={(e) => updateSetting('importExtraFiles', e.target.checked)}
              className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
            />
            <div>
              <span className="text-white font-medium">Import Extra Files</span>
              <p className="text-sm text-gray-400 mt-1">
                Import matching extra files (subtitles, nfo, etc)
              </p>
            </div>
          </label>

          {settings.importExtraFiles && (
            <div>
              <label className="block text-white font-medium mb-2">Extra File Extensions</label>
              <input
                type="text"
                value={settings.extraFileExtensions}
                onChange={(e) => updateSetting('extraFileExtensions', e.target.value)}
                placeholder="srt,nfo,jpg,png"
                className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
              />
              <p className="text-sm text-gray-400 mt-1">
                Comma separated list of extra file extensions to import
              </p>
            </div>
          )}

          {showAdvanced && (
            <>
              <label className="flex items-start space-x-3 cursor-pointer">
                <input
                  type="checkbox"
                  checked={settings.skipFreeSpaceCheck}
                  onChange={(e) => updateSetting('skipFreeSpaceCheck', e.target.checked)}
                  className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                />
                <div>
                  <span className="text-white font-medium">Skip Free Space Check</span>
                  <p className="text-sm text-gray-400 mt-1">
                    Skip checking free space before importing
                  </p>
                  <span className="inline-block mt-1 px-2 py-0.5 bg-yellow-900/30 text-yellow-400 text-xs rounded">
                    Advanced
                  </span>
                </div>
              </label>

              {!settings.skipFreeSpaceCheck && (
                <div>
                  <label className="block text-white font-medium mb-2">Minimum Free Space</label>
                  <div className="flex items-center space-x-2">
                    <input
                      type="number"
                      value={settings.minimumFreeSpace}
                      onChange={(e) => updateSetting('minimumFreeSpace', Number(e.target.value))}
                      className="w-32 px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <span className="text-gray-400">MB</span>
                  </div>
                  <p className="text-sm text-gray-400 mt-1">
                    Prevent import if it would leave less than this amount of free space
                  </p>
                  <span className="inline-block mt-1 px-2 py-0.5 bg-yellow-900/30 text-yellow-400 text-xs rounded">
                    Advanced
                  </span>
                </div>
              )}
            </>
          )}
        </div>
      </div>

      {/* File Management (Advanced) */}
      {showAdvanced && (
        <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
          <h3 className="text-xl font-semibold text-white mb-4">
            File Management
            <span className="ml-2 px-2 py-0.5 bg-yellow-900/30 text-yellow-400 text-xs rounded">
              Advanced
            </span>
          </h3>

          <div className="space-y-4">
            <div>
              <label className="block text-white font-medium mb-2">Recycle Bin Path</label>
              <input
                type="text"
                value={settings.recycleBin}
                onChange={(e) => updateSetting('recycleBin', e.target.value)}
                placeholder="/path/to/recycle/bin"
                className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
              />
              <p className="text-sm text-gray-400 mt-1">
                Files will be moved here instead of being deleted
              </p>
            </div>

            {settings.recycleBin && (
              <div>
                <label className="block text-white font-medium mb-2">Recycle Bin Cleanup</label>
                <div className="flex items-center space-x-2">
                  <input
                    type="number"
                    value={settings.recycleBinCleanup}
                    onChange={(e) => updateSetting('recycleBinCleanup', Number(e.target.value))}
                    className="w-32 px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                  />
                  <span className="text-gray-400">days</span>
                </div>
                <p className="text-sm text-gray-400 mt-1">
                  Set to 0 to disable automatic cleanup
                </p>
              </div>
            )}

            <label className="flex items-start space-x-3 cursor-pointer">
              <input
                type="checkbox"
                checked={settings.setPermissions}
                onChange={(e) => updateSetting('setPermissions', e.target.checked)}
                className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
              />
              <div>
                <span className="text-white font-medium">Set Permissions</span>
                <p className="text-sm text-gray-400 mt-1">
                  Set file permissions during import/rename (Linux/macOS only)
                </p>
              </div>
            </label>

            {settings.setPermissions && (
              <>
                <div>
                  <label className="block text-white font-medium mb-2">chmod Folder</label>
                  <input
                    type="text"
                    value={settings.chmodFolder}
                    onChange={(e) => updateSetting('chmodFolder', e.target.value)}
                    placeholder="755"
                    className="w-32 px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                  />
                </div>

                <div>
                  <label className="block text-white font-medium mb-2">chown Group</label>
                  <input
                    type="text"
                    value={settings.chownGroup}
                    onChange={(e) => updateSetting('chownGroup', e.target.value)}
                    placeholder="media"
                    className="w-64 px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                  />
                </div>
              </>
            )}
          </div>
        </div>
      )}

      {/* Add Root Folder Modal */}
      {showAddFolderModal && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-2xl w-full">
            <div className="flex items-center justify-between mb-6">
              <h3 className="text-2xl font-bold text-white">Add Root Folder</h3>
              <button
                onClick={() => {
                  setShowAddFolderModal(false);
                  setNewFolderPath('');
                }}
                className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
              >
                <XMarkIcon className="w-6 h-6" />
              </button>
            </div>

            <div className="space-y-4">
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Folder Path *</label>
                <div className="flex space-x-2">
                  <input
                    type="text"
                    value={newFolderPath}
                    onChange={(e) => setNewFolderPath(e.target.value)}
                    className="flex-1 px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    placeholder="/data/fightarr or C:\Media\Fightarr"
                  />
                  <button
                    type="button"
                    onClick={() => setShowFileBrowser(true)}
                    className="px-4 py-2 bg-gray-800 hover:bg-gray-700 border border-gray-700 text-white rounded-lg transition-colors flex items-center"
                  >
                    <FolderIcon className="w-5 h-5" />
                  </button>
                </div>
                <p className="text-xs text-gray-500 mt-1">
                  Full path to directory where events will be stored
                </p>
              </div>

              <div className="p-4 bg-blue-950/30 border border-blue-900/50 rounded-lg">
                <p className="text-sm text-blue-300">
                  <strong>Note:</strong> The path will be validated when you click Add. Make sure the directory exists
                  and Fightarr has read/write permissions.
                </p>
              </div>
            </div>

            <div className="mt-6 flex items-center justify-end space-x-3">
              <button
                onClick={() => {
                  setShowAddFolderModal(false);
                  setNewFolderPath('');
                }}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={handleAddFolder}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
              >
                Add Folder
              </button>
            </div>
          </div>
        </div>
      )}

      {/* File Browser Modal */}
      <FileBrowserModal
        isOpen={showFileBrowser}
        onClose={() => setShowFileBrowser(false)}
        onSelect={(path) => {
          setNewFolderPath(path);
          setShowFileBrowser(false);
        }}
        title="Select Root Folder"
      />

      {/* Delete Confirmation Modal */}
      {showDeleteConfirm !== null && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-md w-full">
            <h3 className="text-2xl font-bold text-white mb-4">Delete Root Folder?</h3>
            <p className="text-gray-400 mb-6">
              Are you sure you want to remove this root folder? This will not delete any files, only remove it from Fightarr's configuration.
            </p>
            <div className="flex items-center justify-end space-x-3">
              <button
                onClick={() => setShowDeleteConfirm(null)}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={() => handleDeleteFolder(showDeleteConfirm)}
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
