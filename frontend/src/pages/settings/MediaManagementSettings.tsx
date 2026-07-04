import { useState, useEffect, useRef } from 'react';
import { PlusIcon, FolderIcon, CheckIcon, XMarkIcon, CloudArrowDownIcon } from '@heroicons/react/24/outline';
import { toast } from 'sonner';
import { useQueryClient } from '@tanstack/react-query';
import { apiGet, apiPost, apiPut, apiDelete } from '../../utils/api';
import FileBrowserModal from '../../components/FileBrowserModal';
import SettingsHeader from '../../components/SettingsHeader';
import { useUnsavedChanges } from '../../hooks/useUnsavedChanges';

interface NamingPreset {
  format: string;
  description: string;
  supportsMultiPart: boolean;
}

interface NamingPresets {
  file: Record<string, NamingPreset>;
  folder: Record<string, { format: string; description: string }>;
}

interface MediaManagementSettingsProps {
  showAdvanced?: boolean;
}

interface RootFolder {
  id: number;
  path: string;
  accessible: boolean;
  freeSpace: number;
  totalSpace: number;
  defaultQualityProfileId?: number | null;
  defaultDownloadClientCategory?: string | null;
}

interface QualityProfileOption {
  id: number;
  name: string;
}

interface MediaManagementSettingsData {
  renameEvents: boolean;
  replaceIllegalCharacters: boolean;
  enableMultiPartEpisodes: boolean;
  standardFileFormat: string;
  // Granular folder options - cascading hierarchy
  createLeagueFolders: boolean;
  createSeasonFolders: boolean;
  createEventFolders: boolean;
  leagueFolderFormat: string;
  seasonFolderFormat: string;
  eventFolderFormat: string;
  deleteEmptyFolders: boolean;
  reorganizeFolders: boolean;
  skipFreeSpaceCheck: boolean;
  minimumFreeSpace: number;
  useHardlinks: boolean;
  copyFiles: boolean;
  importExtraFiles: boolean;
  extraFileExtensions: string;
  userRejectedExtensions: string;
  changeFileDate: string;
  recycleBin: string;
  recycleBinCleanup: number;
  setPermissions: boolean;
  chmodFolder: string;
  chownGroup: string;
  enableEventRetention: boolean;
  eventRetentionDays: number;
}

export default function MediaManagementSettings({ showAdvanced: propShowAdvanced = false }: MediaManagementSettingsProps) {
  const queryClient = useQueryClient();
  const [rootFolders, setRootFolders] = useState<RootFolder[]>([]);
  const [qualityProfileOptions, setQualityProfileOptions] = useState<QualityProfileOption[]>([]);
  // Per-root cache for the unmapped-folders endpoint. The list is opt-in
  // so we don't walk the disk unsolicited every time the user opens the
  // Settings page.
  const [unmappedByRoot, setUnmappedByRoot] = useState<Record<number, { loading: boolean; folders: { name: string; path: string }[]; error?: string; expanded: boolean }>>({});
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [showAddFolderModal, setShowAddFolderModal] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState<number | null>(null);
  const [newFolderPath, setNewFolderPath] = useState('');
  // Error from the most recent add-folder attempt. Surfaced inline in
  // the Add modal so a validator rejection (write-test failed, system
  // path, etc.) doesn't silently look like nothing happened.
  const [addFolderError, setAddFolderError] = useState<string | null>(null);
  const [addingFolder, setAddingFolder] = useState(false);
  const [showFileBrowser, setShowFileBrowser] = useState(false);
  const [hasUnsavedChanges, setHasUnsavedChanges] = useState(false);
  const initialSettings = useRef<MediaManagementSettingsData | null>(null);
  const [namingPresets, setNamingPresets] = useState<NamingPresets | null>(null);
  const [selectedFilePreset, setSelectedFilePreset] = useState<string>('');

  // Show Advanced toggle - persisted per page to localStorage
  const [showAdvanced, setShowAdvanced] = useState(() => {
    const saved = localStorage.getItem('sportarr-showAdvanced-mediamanagement');
    return saved === 'true' || propShowAdvanced;
  });

  // Persist showAdvanced to localStorage when changed
  useEffect(() => {
    localStorage.setItem('sportarr-showAdvanced-mediamanagement', showAdvanced.toString());
  }, [showAdvanced]);

  // Use unsaved changes hook
  const { blockNavigation } = useUnsavedChanges(hasUnsavedChanges);

  // Media Management Settings stored in database
  const [settings, setSettings] = useState<MediaManagementSettingsData>({
    renameEvents: false,
    replaceIllegalCharacters: true,
    enableMultiPartEpisodes: true,
    standardFileFormat: '{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full}',
    // Granular folder options - default: league/season enabled, event disabled
    createLeagueFolders: true,
    createSeasonFolders: true,
    createEventFolders: false,
    leagueFolderFormat: '{Series}',
    seasonFolderFormat: 'Season {Season}',
    eventFolderFormat: '{Event Title} ({Year}-{Month}-{Day}) E{Episode}',
    deleteEmptyFolders: false,
    reorganizeFolders: false,
    skipFreeSpaceCheck: false,
    minimumFreeSpace: 100,
    useHardlinks: true,
    copyFiles: false,
    importExtraFiles: false,
    extraFileExtensions: 'srt,nfo',
    userRejectedExtensions: '',
    changeFileDate: 'None',
    recycleBin: '',
    recycleBinCleanup: 7,
    setPermissions: false,
    chmodFolder: '755',
    chownGroup: '',
    enableEventRetention: false,
    eventRetentionDays: 30,
  });

  // Load settings and root folders from API on mount
  useEffect(() => {
    loadSettings();
    fetchRootFolders();
    loadNamingPresets();
  }, []);

  const loadNamingPresets = async () => {
    try {
      const response = await apiGet(`/api/trash/naming-presets?enableMultiPartEpisodes=${settings.enableMultiPartEpisodes}`);
      if (response.ok) {
        const data = await response.json();
        setNamingPresets(data);
      }
    } catch (error) {
      console.error('Failed to load naming presets:', error);
    }
  };

  // Reload presets when multi-part setting changes
  useEffect(() => {
    if (namingPresets) {
      loadNamingPresets();
    }
  }, [settings.enableMultiPartEpisodes]);

  const handleApplyFilePreset = (presetKey: string) => {
    if (!namingPresets?.file?.[presetKey]) return;
    const preset = namingPresets.file[presetKey];
    updateSetting('standardFileFormat', preset.format);
    setSelectedFilePreset(presetKey);
    toast.success('Naming preset applied', {
      description: preset.description,
    });
  };

  const loadSettings = async () => {
    try {
      const response = await apiGet('/api/settings');
      if (response.ok) {
        const data = await response.json();
        if (data.mediaManagementSettings) {
          const parsed = JSON.parse(data.mediaManagementSettings);
          // Debug logging to diagnose folder settings persistence issue
          console.log('[MediaManagement] Raw mediaManagementSettings from API:', data.mediaManagementSettings);
          console.log('[MediaManagement] Parsed settings:', parsed);
          console.log('[MediaManagement] Folder settings - createLeagueFolders:', parsed.createLeagueFolders,
            ', createSeasonFolders:', parsed.createSeasonFolders,
            ', createEventFolders:', parsed.createEventFolders);
          setSettings(parsed);
          initialSettings.current = parsed;
          setHasUnsavedChanges(false);
        }
      }
    } catch (error) {
      console.error('Failed to load media management settings:', error);
    }
  };

  // Detect changes
  useEffect(() => {
    if (!initialSettings.current) return;
    const hasChanges = JSON.stringify(settings) !== JSON.stringify(initialSettings.current);
    setHasUnsavedChanges(hasChanges);
  }, [settings]);

  const fetchRootFolders = async () => {
    try {
      const [foldersRes, profilesRes] = await Promise.all([
        apiGet('/api/rootfolder'),
        apiGet('/api/qualityprofile').catch(() => null),
      ]);
      if (foldersRes.ok) {
        const data = await foldersRes.json();
        setRootFolders(data);
      }
      if (profilesRes && profilesRes.ok) {
        const profiles = await profilesRes.json();
        setQualityProfileOptions(Array.isArray(profiles) ? profiles : []);
      }
    } catch (error) {
      console.error('Failed to fetch root folders:', error);
    } finally {
      setLoading(false);
    }
  };

  // PUT the per-root defaults back to /api/rootfolder/{id}. Called by the
  // inline edits on each root folder card. We patch local state
  // optimistically so the UI doesn't flash stale values; any error from
  // the server reverts and surfaces a toast.
  const updateRootFolderDefaults = async (
    folderId: number,
    patch: { defaultQualityProfileId?: number | null; defaultDownloadClientCategory?: string | null },
  ) => {
    const original = rootFolders.find(rf => rf.id === folderId);
    if (!original) return;
    const optimistic: RootFolder = { ...original, ...patch };
    setRootFolders(prev => prev.map(rf => (rf.id === folderId ? optimistic : rf)));
    try {
      const response = await apiPut(`/api/rootfolder/${folderId}`, optimistic);
      if (!response.ok) {
        const body = await response.json().catch(() => null);
        window.alert(`Failed to save root-folder defaults: ${body?.error ?? response.statusText}`);
        setRootFolders(prev => prev.map(rf => (rf.id === folderId ? original : rf)));
      } else {
        const fresh = await response.json();
        setRootFolders(prev => prev.map(rf => (rf.id === folderId ? fresh : rf)));
      }
    } catch (err) {
      console.error('Failed to update root folder defaults:', err);
      setRootFolders(prev => prev.map(rf => (rf.id === folderId ? original : rf)));
    }
  };

  const formatBytes = (bytes: number) => {
    const gb = bytes / (1024 * 1024 * 1024);
    return `${gb.toFixed(2)} GB`;
  };

  const handleAddFolder = async () => {
    if (!newFolderPath.trim()) {
      setAddFolderError('Folder path is required.');
      return;
    }

    setAddFolderError(null);
    setAddingFolder(true);
    try {
      const response = await apiPost('/api/rootfolder', {
        path: newFolderPath.trim(),
      });

      if (response.ok) {
        const newFolder = await response.json();
        setRootFolders(prev => [...prev, newFolder]);
        setShowAddFolderModal(false);
        setNewFolderPath('');
        setAddFolderError(null);
      } else {
        const body = await response.json().catch(() => null);
        const message = body?.error ?? `HTTP ${response.status}: ${response.statusText}`;
        console.error('Failed to add root folder:', message);
        setAddFolderError(message);
      }
    } catch (error) {
      console.error('Failed to add folder:', error);
      setAddFolderError((error as Error)?.message ?? 'Network error contacting Sportarr.');
    } finally {
      setAddingFolder(false);
    }
  };

  const handleDeleteFolder = async (id: number, force: boolean = false) => {
    try {
      const url = force ? `/api/rootfolder/${id}?force=true` : `/api/rootfolder/${id}`;
      const response = await apiDelete(url);

      if (response.ok) {
        setRootFolders(prev => prev.filter(f => f.id !== id));
        setShowDeleteConfirm(null);
        return;
      }

      // 409: leagues are still bound to this root folder. Surface the
      // conflict to the user with the option to force-detach the
      // bindings before deleting. The API returns the offending league
      // names so the prompt can list them.
      if (response.status === 409) {
        const body = await response.json().catch(() => null);
        const leagueNames = (body?.leagues ?? [])
          .map((l: { id: number; name: string }) => `  • ${l.name} (id ${l.id})`)
          .join('\n');
        const confirmed = window.confirm(
          `${body?.error ?? 'Root folder is still bound to leagues.'}\n\n` +
          `${leagueNames}\n\n` +
          `Click OK to detach the bindings and delete the root folder anyway. ` +
          `The leagues will fall back to free-space selection on their next import. ` +
          `Click Cancel to leave everything as is.`
        );
        if (confirmed) {
          await handleDeleteFolder(id, true);
        }
        return;
      }

      // Other failure path: surface whatever the server said.
      const errBody = await response.json().catch(() => null);
      window.alert(`Failed to delete folder: ${errBody?.error ?? response.statusText}`);
    } catch (error) {
      console.error('Failed to delete folder:', error);
      window.alert('Failed to delete folder. See console for details.');
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

      // Invalidate config query so other pages (like LeagueDetailPage) get updated settings
      await queryClient.invalidateQueries({ queryKey: ['config'] });

      // Reset unsaved changes flag
      initialSettings.current = settings;
      setHasUnsavedChanges(false);
    } catch (error) {
      console.error('Failed to save settings:', error);
      toast.error('Save Failed', {
        description: 'Failed to save settings. Please try again.',
      });
    } finally{
      setSaving(false);
    }
  };

  const updateSetting = <K extends keyof MediaManagementSettingsData>(
    key: K,
    value: MediaManagementSettingsData[K]
  ) => {
    setSettings(prev => ({ ...prev, [key]: value }));
  };

  // Track previous enableMultiPartEpisodes value for toggle detection
  const prevEnableMultiPart = useRef<boolean | null>(null);

  // Auto-manage {Part} token when EnableMultiPartEpisodes is toggled
  useEffect(() => {
    // Skip the initial load (when prevEnableMultiPart hasn't been set yet)
    if (prevEnableMultiPart.current === null) {
      prevEnableMultiPart.current = settings.enableMultiPartEpisodes;
      return;
    }

    const previousValue = prevEnableMultiPart.current;
    const currentValue = settings.enableMultiPartEpisodes;

    // Only update if the checkbox value actually changed
    if (previousValue !== currentValue) {
      const currentFormat = settings.standardFileFormat || '';

      // {Part Name} (human-label variant) counts as having a part token, so
      // users who swapped {Part} for {Part Name} don't get {Part} re-inserted.
      const hasAnyPartToken = currentFormat.includes('{Part}') || currentFormat.includes('{Part Name}');

      if (currentValue && !hasAnyPartToken) {
        // ENABLING: Add {Part} token
        let newFormat: string;
        if (currentFormat.includes('{Episode}')) {
          // Insert after {Episode} if it exists
          newFormat = currentFormat.replace('{Episode}', '{Episode}{Part}');
        } else {
          // Otherwise append to the end of the format
          newFormat = currentFormat.trim() + '{Part}';
        }
        setSettings(prev => ({ ...prev, standardFileFormat: newFormat }));
      } else if (!currentValue && hasAnyPartToken) {
        // DISABLING: Remove part token(s)
        const newFormat = currentFormat.replace('{Part Name}', '').replace('{Part}', '');
        setSettings(prev => ({ ...prev, standardFileFormat: newFormat }));
      }

      // Update the previous value ref
      prevEnableMultiPart.current = currentValue;
    }
  }, [settings.enableMultiPartEpisodes]);

  // Note: In-app navigation blocking would require React Router's unstable_useBlocker
  // For now, we only block browser refresh/close via the useUnsavedChanges hook

  // Toggle the Unmapped Folders panel for a given root folder. First open
  // fetches the list lazily; subsequent opens reuse the cached result.
  const toggleUnmapped = async (folderId: number) => {
    const current = unmappedByRoot[folderId];
    if (current?.expanded) {
      setUnmappedByRoot(prev => ({ ...prev, [folderId]: { ...current, expanded: false } }));
      return;
    }
    if (current?.folders) {
      setUnmappedByRoot(prev => ({ ...prev, [folderId]: { ...current, expanded: true } }));
      return;
    }
    setUnmappedByRoot(prev => ({ ...prev, [folderId]: { loading: true, folders: [], expanded: true } }));
    try {
      const response = await apiGet(`/api/rootfolder/${folderId}/unmappedfolders`);
      if (!response.ok) {
        const err = await response.json().catch(() => ({}));
        setUnmappedByRoot(prev => ({
          ...prev,
          [folderId]: { loading: false, folders: [], expanded: true, error: err?.error ?? `HTTP ${response.status}` },
        }));
        return;
      }
      const body = await response.json();
      setUnmappedByRoot(prev => ({
        ...prev,
        [folderId]: { loading: false, folders: body?.unmapped ?? [], expanded: true },
      }));
    } catch (err) {
      setUnmappedByRoot(prev => ({
        ...prev,
        [folderId]: { loading: false, folders: [], expanded: true, error: (err as Error).message ?? 'Failed to load' },
      }));
    }
  };

  return (
    <div>
      <SettingsHeader
        title="Media Management"
        subtitle="Settings for file naming, root folders, and file management"
        onSave={handleSave}
        isSaving={saving}
        hasUnsavedChanges={hasUnsavedChanges}
      >
        {/* Show Advanced Toggle - like Sonarr */}
        <label className="flex items-center space-x-2 cursor-pointer text-sm">
          <input
            type="checkbox"
            checked={showAdvanced}
            onChange={(e) => setShowAdvanced(e.target.checked)}
            className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600 focus:ring-offset-gray-900"
          />
          <span className="text-gray-300">Show Advanced</span>
        </label>
      </SettingsHeader>

      <div className="max-w-4xl mx-auto px-6">

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
          Root folders where Sportarr will store sports events
        </p>

        <div className="space-y-2">
          {rootFolders.map((folder) => {
            const unmappedState = unmappedByRoot[folder.id];
            return (
              <div
                key={folder.id}
                className="bg-black/30 rounded-lg border border-gray-800"
              >
                <div className="flex items-center justify-between p-4">
                  <div className="flex items-center flex-1 min-w-0">
                    <FolderIcon className="w-5 h-5 text-red-400 mr-3 flex-shrink-0" />
                    <div className="flex-1 min-w-0">
                      <p className="text-white font-medium truncate">{folder.path}</p>
                      <p className="text-sm text-gray-400">
                        {folder.totalSpace > 0
                          ? `${formatBytes(folder.totalSpace - folder.freeSpace)} used · ${formatBytes(folder.freeSpace)} free of ${formatBytes(folder.totalSpace)}`
                          : `Free Space: ${formatBytes(folder.freeSpace)}`}
                      </p>
                      {/* Disk-usage bar — purely visual, computed live each
                          fetch. Bar fills red when usage exceeds 90% so a
                          full disk pops out at a glance. */}
                      {folder.accessible && folder.totalSpace > 0 && (() => {
                        const usedPct = Math.min(100, Math.max(0, ((folder.totalSpace - folder.freeSpace) / folder.totalSpace) * 100));
                        const barColor = usedPct >= 95 ? 'bg-red-600' : usedPct >= 85 ? 'bg-yellow-500' : 'bg-green-600';
                        return (
                          <div className="mt-2 h-1.5 w-full bg-gray-800 rounded overflow-hidden">
                            <div className={`h-full ${barColor}`} style={{ width: `${usedPct.toFixed(1)}%` }} />
                          </div>
                        );
                      })()}
                    </div>
                  </div>
                  <div className="flex items-center space-x-3 flex-shrink-0">
                    {folder.accessible ? (
                      <CheckIcon className="w-5 h-5 text-green-500" />
                    ) : (
                      <XMarkIcon className="w-5 h-5 text-red-500" />
                    )}
                    <button
                      onClick={() => toggleUnmapped(folder.id)}
                      disabled={!folder.accessible}
                      className="text-gray-400 hover:text-white text-sm transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                      title={folder.accessible ? 'Show subfolders not yet mapped to a league' : 'Folder is not accessible'}
                    >
                      {unmappedState?.expanded ? 'Hide unmapped' : 'Show unmapped'}
                    </button>
                    <button
                      onClick={() => setShowDeleteConfirm(folder.id)}
                      className="text-gray-400 hover:text-red-400 text-sm transition-colors"
                    >
                      Delete
                    </button>
                  </div>
                </div>

                {/* Per-root defaults (Phase 4): both optional. Quality
                    Profile is suggested at league add time; download
                    client category overrides the client's configured
                    Category at grab time for any league bound to this
                    root. Saves on change via PUT /api/rootfolder/{id}. */}
                <div className="px-4 pb-3 grid grid-cols-1 md:grid-cols-2 gap-3 border-t border-gray-800/40 pt-3">
                  <div>
                    <label className="block text-xs text-gray-400 mb-1">Default Quality Profile</label>
                    <select
                      value={folder.defaultQualityProfileId ?? ''}
                      onChange={(e) =>
                        updateRootFolderDefaults(folder.id, {
                          defaultQualityProfileId: e.target.value ? parseInt(e.target.value) : null,
                        })
                      }
                      className="w-full px-2 py-1.5 bg-black border border-gray-700 text-gray-200 text-sm rounded focus:outline-none focus:border-red-600 focus:ring-1 focus:ring-red-600"
                    >
                      <option value="">No default (use global)</option>
                      {qualityProfileOptions.map(qp => (
                        <option key={qp.id} value={qp.id}>{qp.name}</option>
                      ))}
                    </select>
                  </div>
                  <div>
                    <label className="block text-xs text-gray-400 mb-1">Download Client Category Override</label>
                    <input
                      type="text"
                      value={folder.defaultDownloadClientCategory ?? ''}
                      onChange={(e) =>
                        setRootFolders(prev =>
                          prev.map(rf =>
                            rf.id === folder.id
                              ? { ...rf, defaultDownloadClientCategory: e.target.value }
                              : rf
                          )
                        )
                      }
                      onBlur={(e) =>
                        updateRootFolderDefaults(folder.id, {
                          defaultDownloadClientCategory: e.target.value.trim() === '' ? null : e.target.value.trim(),
                        })
                      }
                      placeholder="(none — use download client's category)"
                      className="w-full px-2 py-1.5 bg-black border border-gray-700 text-gray-200 text-sm rounded focus:outline-none focus:border-red-600 focus:ring-1 focus:ring-red-600"
                    />
                  </div>
                </div>
                {unmappedState?.expanded && (
                  <div className="px-4 pb-4 border-t border-gray-800/60 pt-3">
                    {unmappedState.loading ? (
                      <p className="text-sm text-gray-400">Scanning…</p>
                    ) : unmappedState.error ? (
                      <p className="text-sm text-red-400">{unmappedState.error}</p>
                    ) : unmappedState.folders.length === 0 ? (
                      <p className="text-sm text-gray-500">No unmapped subfolders — every directory under this root matches an existing league.</p>
                    ) : (
                      <>
                        <p className="text-xs text-gray-500 mb-2">
                          {unmappedState.folders.length} subfolder{unmappedState.folders.length === 1 ? '' : 's'} not currently associated with a league. Use Library Import to adopt them.
                        </p>
                        <ul className="divide-y divide-gray-800/60">
                          {unmappedState.folders.map(uf => (
                            <li key={uf.path} className="flex items-center justify-between py-2">
                              <div className="flex items-center min-w-0 flex-1">
                                <FolderIcon className="w-4 h-4 text-yellow-500 mr-2 flex-shrink-0" />
                                <span className="text-sm text-gray-200 truncate" title={uf.path}>{uf.name}</span>
                              </div>
                              <a
                                href={`/library-import?path=${encodeURIComponent(uf.path)}`}
                                className="px-2 py-1 text-xs bg-red-600/80 hover:bg-red-600 text-white rounded transition-colors flex-shrink-0 ml-3"
                              >
                                Library Import
                              </a>
                            </li>
                          ))}
                        </ul>
                      </>
                    )}
                  </div>
                )}
              </div>
            );
          })}
        </div>

        {rootFolders.length === 0 && (
          <div className="text-center py-12">
            <FolderIcon className="w-16 h-16 text-gray-700 mx-auto mb-4" />
            <p className="text-gray-500 mb-2">No root folders configured</p>
            <p className="text-sm text-gray-400">
              Add at least one root folder where Sportarr will store events
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

          <label className="flex items-start space-x-3 cursor-pointer">
            <input
              type="checkbox"
              checked={settings.enableMultiPartEpisodes}
              onChange={(e) => updateSetting('enableMultiPartEpisodes', e.target.checked)}
              className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
            />
            <div>
              <span className="text-white font-medium">Enable Multi-Part Episodes</span>
              <p className="text-sm text-gray-400 mt-1">
                Detect and name multi-part episodes for Fighting sports (Early Prelims, Prelims, Main Card)
              </p>
              <div className="mt-2 px-3 py-2 bg-blue-950/30 border border-blue-900/50 rounded text-xs">
                <p className="text-blue-300 font-medium mb-1">Plex TV Show Structure:</p>
                <p className="text-gray-400">MMA League - s2024e12 - pt1 - Event 100 Main Event - Bluray-1080p.mkv (Early Prelims)</p>
                <p className="text-gray-400">MMA League - s2024e12 - pt2 - Event 100 Main Event - Bluray-1080p.mkv (Prelims)</p>
                <p className="text-gray-400">MMA League - s2024e12 - pt3 - Event 100 Main Event - Bluray-1080p.mkv (Main Card)</p>
              </div>
            </div>
          </label>

          {settings.renameEvents && (
            <>
              <div>
                <div className="flex items-center justify-between mb-2">
                  <label className="block text-white font-medium">Standard Event Format</label>
                  {namingPresets?.file && Object.keys(namingPresets.file).length > 0 && (
                    <div className="flex items-center gap-2">
                      <CloudArrowDownIcon className="w-4 h-4 text-purple-400" />
                      <select
                        value={selectedFilePreset}
                        onChange={(e) => handleApplyFilePreset(e.target.value)}
                        className="px-3 py-1 bg-gray-800 border border-purple-700 rounded text-sm text-purple-200 focus:outline-none focus:border-purple-500"
                      >
                        <option value="" className="bg-gray-800 text-gray-300">TRaSH Naming Presets...</option>
                        {Object.entries(namingPresets.file).map(([key, preset]) => (
                          <option key={key} value={key} className="bg-gray-800 text-white">
                            {key.replace(/-/g, ' ').replace(/\b\w/g, l => l.toUpperCase())}
                            {preset.supportsMultiPart ? ' (Multi-Part)' : ''}
                          </option>
                        ))}
                      </select>
                    </div>
                  )}
                </div>
                <div className="relative">
                  <input
                    type="text"
                    value={settings.standardFileFormat}
                    onChange={(e) => {
                      updateSetting('standardFileFormat', e.target.value);
                      setSelectedFilePreset(''); // Clear preset selection when manually editing
                    }}
                    className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600 font-mono"
                    placeholder="{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full}"
                  />
                </div>

                {/* Token Helper */}
                <div className="mt-3 p-4 bg-black/30 rounded-lg border border-gray-800">
                  <p className="text-sm font-medium text-gray-300 mb-2">Available Tokens (click to insert):</p>
                  <div className="grid grid-cols-2 md:grid-cols-3 gap-2">
                    {[
                      { token: '{Series}', desc: 'MMA League', category: 'Plex' },
                      { token: '{Season}', desc: 's2024', category: 'Plex' },
                      { token: '{Episode}', desc: 'e12', category: 'Plex' },
                      { token: '{Part}', desc: 'pt1/pt2/pt3', category: 'Plex' },
                      { token: '{Part Name}', desc: 'Prelims/Main Card', category: 'Plex' },
                      { token: '{Event Title}', desc: 'Event 100', category: 'Event' },
                      { token: '{Event Date}', desc: '2024-04-13', category: 'Event' },
                      { token: '{Quality Full}', desc: 'Bluray-1080p', category: 'Quality' },
                      { token: '{Release Group}', desc: 'GROUP', category: 'Release' },
                    ].map((item) => (
                      <button
                        key={item.token}
                        onClick={() => {
                          const input = document.querySelector('input[placeholder*="Series"]') as HTMLInputElement;
                          if (input) {
                            const currentFormat = settings.standardFileFormat || '';
                            const cursorPos = input.selectionStart || currentFormat.length;
                            const newValue =
                              currentFormat.slice(0, cursorPos) +
                              item.token +
                              currentFormat.slice(cursorPos);
                            updateSetting('standardFileFormat', newValue);
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
                    {(settings.standardFileFormat || '')
                      .replace(/{Series}/g, 'MMA League')
                      .replace(/{Season}/g, 's2024')
                      .replace(/{Episode}/g, 'e12')
                      .replace(/{Part}/g, settings.enableMultiPartEpisodes ? ' - pt3' : '')
                      .replace(/{Event Title}/g, 'Event 100 Main Event')
                      .replace(/{League}/g, 'MMA League')
                      .replace(/{Event Date}/g, '2024-11-16')
                      .replace(/{Quality Full}/g, 'Bluray-1080p')
                      .replace(/{Release Group}/g, 'GROUP')
                    }.mkv
                  </p>
                  <p className="text-xs text-gray-500 mt-2">
                    This shows how your events will be named with the current format
                    {settings.enableMultiPartEpisodes && <span className="text-blue-400"> (with multi-part enabled, showing Main Card example)</span>}
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
        <p className="text-sm text-gray-400 mb-4">
          Control folder hierarchy for imported events. Each level is optional and depends on the previous level being enabled.
        </p>

        <div className="space-y-4">
          {/* Create League Folders */}
          <label className="flex items-start space-x-3 cursor-pointer">
            <input
              type="checkbox"
              checked={settings.createLeagueFolders}
              onChange={(e) => {
                updateSetting('createLeagueFolders', e.target.checked);
                // Cascade disable child options when parent is disabled
                if (!e.target.checked) {
                  updateSetting('createSeasonFolders', false);
                  updateSetting('createEventFolders', false);
                }
              }}
              className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
            />
            <div className="flex-1">
              <span className="text-white font-medium">Create League Folders</span>
              <p className="text-sm text-gray-400 mt-1">
                Create a folder for each league (e.g., <code className="text-purple-400 bg-gray-800 px-1 rounded">/UFC/</code>, <code className="text-purple-400 bg-gray-800 px-1 rounded">/Premier League/</code>)
              </p>
            </div>
          </label>

          {/* Create Season Folders - only visible if League Folders enabled */}
          {settings.createLeagueFolders && (
            <label className="flex items-start space-x-3 cursor-pointer ml-8 border-l-2 border-gray-700 pl-4">
              <input
                type="checkbox"
                checked={settings.createSeasonFolders}
                onChange={(e) => {
                  updateSetting('createSeasonFolders', e.target.checked);
                  // Cascade disable child option when parent is disabled
                  if (!e.target.checked) {
                    updateSetting('createEventFolders', false);
                  }
                }}
                className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
              />
              <div className="flex-1">
                <span className="text-white font-medium">Create Season Folders</span>
                <p className="text-sm text-gray-400 mt-1">
                  Create a season folder within each league (e.g., <code className="text-purple-400 bg-gray-800 px-1 rounded">/UFC/Season 2024/</code>)
                </p>
              </div>
            </label>
          )}

          {/* Create Event Folders - only visible if Season Folders enabled */}
          {settings.createLeagueFolders && settings.createSeasonFolders && (
            <label className="flex items-start space-x-3 cursor-pointer ml-16 border-l-2 border-gray-700 pl-4">
              <input
                type="checkbox"
                checked={settings.createEventFolders}
                onChange={(e) => updateSetting('createEventFolders', e.target.checked)}
                className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
              />
              <div className="flex-1">
                <span className="text-white font-medium">Create Event Folders</span>
                <p className="text-sm text-gray-400 mt-1">
                  Create a folder for each event (e.g., <code className="text-purple-400 bg-gray-800 px-1 rounded">/UFC/Season 2024/UFC 310/</code>)
                </p>
                <p className="text-xs text-gray-500 mt-1">
                  Multi-part events (Early Prelims, Prelims, Main Card) will be grouped in the same event folder.
                </p>
              </div>
            </label>
          )}

          {/* Event Folder Format - only visible when event folders are enabled */}
          {settings.createLeagueFolders && settings.createSeasonFolders && settings.createEventFolders && (
            <div className="ml-16 border-l-2 border-gray-700 pl-4">
              <label className="block text-sm font-medium text-gray-300 mb-2">Event Folder Format</label>
              <input
                type="text"
                value={settings.eventFolderFormat}
                onChange={(e) => updateSetting('eventFolderFormat', e.target.value)}
                placeholder="{Event Title} ({Year}-{Month}-{Day}) E{Episode}"
                className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white font-mono text-sm focus:outline-none focus:border-red-600"
              />
              <p className="text-xs text-gray-500 mt-1">
                Tokens: <code className="text-purple-400">{'{Event Title}'}</code>, <code className="text-purple-400">{'{Event Weekend Title}'}</code>,{' '}
                <code className="text-purple-400">{'{Year}'}</code>, <code className="text-purple-400">{'{Month}'}</code>,{' '}
                <code className="text-purple-400">{'{Day}'}</code>, <code className="text-purple-400">{'{Episode}'}</code>.
                Use <code className="text-purple-400">{'{Event Weekend Title}'}</code> to group every session of a motorsport
                weekend (Practice, Qualifying, Sprint, Race) into one folder like <code className="text-purple-400">Monaco Grand Prix/</code>.
                The default includes E{'{Episode}'} so same-day events keep separate folders.
              </p>
            </div>
          )}

          {/* Path Preview */}
          <div className="mt-4 p-4 bg-gradient-to-r from-blue-950/30 to-purple-950/30 border border-blue-900/50 rounded-lg">
            <p className="text-sm font-medium text-blue-300 mb-2">Folder Structure Preview:</p>
            <p className="text-white font-mono text-sm">
              /root/
              {settings.createLeagueFolders && <span className="text-green-400">UFC/</span>}
              {settings.createLeagueFolders && settings.createSeasonFolders && <span className="text-yellow-400">Season 2024/</span>}
              {settings.createLeagueFolders && settings.createSeasonFolders && settings.createEventFolders && <span className="text-purple-400">UFC 310/</span>}
              <span className="text-gray-400">filename.mkv</span>
            </p>
            <p className="text-xs text-gray-500 mt-2">
              {!settings.createLeagueFolders && "All files will be stored directly in the root folder."}
              {settings.createLeagueFolders && !settings.createSeasonFolders && "Files organized by league only."}
              {settings.createLeagueFolders && settings.createSeasonFolders && !settings.createEventFolders && "Files organized by league and season (Plex TV show style)."}
              {settings.createLeagueFolders && settings.createSeasonFolders && settings.createEventFolders && "Files organized by league, season, and event."}
            </p>
          </div>

          {/* Reorganize Folders (when renaming) */}
          <label className="flex items-start space-x-3 cursor-pointer mt-4">
            <input
              type="checkbox"
              checked={settings.reorganizeFolders}
              onChange={(e) => updateSetting('reorganizeFolders', e.target.checked)}
              className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
            />
            <div>
              <span className="text-white font-medium">Reorganize Folders on Rename</span>
              <p className="text-sm text-gray-400 mt-1">
                When renaming files, also move them to match the current folder structure settings above.
              </p>
              <p className="text-xs text-yellow-400 mt-1">
                Warning: Enabling this will move existing files to new locations when you trigger a rename operation.
              </p>
            </div>
          </label>

          {showAdvanced && (
            <label className="flex items-start space-x-3 cursor-pointer mt-4">
              <input
                type="checkbox"
                checked={settings.deleteEmptyFolders}
                onChange={(e) => updateSetting('deleteEmptyFolders', e.target.checked)}
                className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
              />
              <div>
                <span className="text-white font-medium">Delete Empty Folders</span>
                <p className="text-sm text-gray-400 mt-1">
                  Delete empty folders during disk scan
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
              checked={settings.copyFiles}
              onChange={(e) => updateSetting('copyFiles', e.target.checked)}
              className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
            />
            <div>
              <span className="text-white font-medium">Copy Files (instead of Move)</span>
              <p className="text-sm text-gray-400 mt-1">
                <strong>Disabled (recommended):</strong> Files are moved from downloads to your library, freeing up space.<br/>
                <strong>Enabled:</strong> Files are copied, keeping the original in downloads (uses more disk space).<br/>
                <span className="text-gray-500 italic">Debrid users: This setting doesn't affect you - symlinks are always handled correctly.</span>
              </p>
            </div>
          </label>

          {settings.copyFiles && (
            <div className="bg-blue-900/30 border border-blue-600/50 rounded-lg p-3 ml-8">
              <p className="text-blue-400 text-sm">
                <strong>Torrent Seeding:</strong> Source files will be preserved in your download folder for continued seeding,
                regardless of the "Remove Completed Downloads" setting.
              </p>
            </div>
          )}

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

          {/* Pairs with the FailDownloads "User-Defined Extensions"
              category set per indexer. Listed here in Importing because
              that's where it lives in the upstream UX — and because
              ExtraFileExtensions / UserRejectedExtensions are the two
              "the user cares about file extensions" knobs and they
              read more naturally side by side. */}
          <div>
            <label className="block text-white font-medium mb-2">User-Rejected Extensions</label>
            <input
              type="text"
              value={settings.userRejectedExtensions ?? ''}
              onChange={(e) => updateSetting('userRejectedExtensions', e.target.value)}
              placeholder=".nfo, .url, .txt"
              className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
            />
            <p className="text-sm text-gray-400 mt-1">
              Comma-separated extensions to count against an indexer's <em>Fail Downloads → User-Defined Extensions</em> policy. Leave blank to disable that category.
            </p>
          </div>

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

      {/* Event Retention */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <h3 className="text-xl font-semibold text-white mb-4">Event Retention</h3>

        <div className="space-y-4">
          <label className="flex items-start space-x-3 cursor-pointer">
            <input
              type="checkbox"
              checked={settings.enableEventRetention}
              onChange={(e) => updateSetting('enableEventRetention', e.target.checked)}
              className="mt-1 w-5 h-5 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
            />
            <div>
              <span className="text-white font-medium">Auto-Unmonitor and Delete Old Events</span>
              <p className="text-sm text-gray-400 mt-1">
                Once an event's air date is older than the threshold below, unmonitor it and delete its file
                so it stops taking up disk space and being searched for.
              </p>
            </div>
          </label>

          {settings.enableEventRetention && (
            <div>
              <label className="block text-white font-medium mb-2">Delete After</label>
              <div className="flex items-center space-x-2">
                <input
                  type="number"
                  min={1}
                  value={settings.eventRetentionDays}
                  onChange={(e) => updateSetting('eventRetentionDays', Math.max(1, Number(e.target.value)))}
                  className="w-32 px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                />
                <span className="text-gray-400">days after the event airs</span>
              </div>
              <p className="text-sm text-gray-400 mt-1">
                Checked once a day. Files are moved to the recycle bin if one is configured below, otherwise
                deleted outright.
              </p>
            </div>
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
                    onChange={(e) => {
                      setNewFolderPath(e.target.value);
                      if (addFolderError) setAddFolderError(null);
                    }}
                    className="flex-1 px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    placeholder="/data/sportarr or C:\Media\Sportarr"
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
                  and Sportarr has read/write permissions.
                </p>
              </div>

              {addFolderError && (
                <div className="p-4 bg-red-950/30 border border-red-700/50 rounded-lg">
                  <p className="text-sm text-red-300 whitespace-pre-wrap">
                    <strong>Couldn't add folder:</strong> {addFolderError}
                  </p>
                </div>
              )}
            </div>

            <div className="mt-6 flex items-center justify-end space-x-3">
              <button
                onClick={() => {
                  setShowAddFolderModal(false);
                  setNewFolderPath('');
                  setAddFolderError(null);
                }}
                disabled={addingFolder}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors disabled:opacity-50"
              >
                Cancel
              </button>
              <button
                onClick={handleAddFolder}
                disabled={addingFolder}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors disabled:opacity-50"
              >
                {addingFolder ? 'Adding…' : 'Add Folder'}
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
              Are you sure you want to remove this root folder? This will not delete any files, only remove it from Sportarr's configuration.
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
    </div>
  );
}
