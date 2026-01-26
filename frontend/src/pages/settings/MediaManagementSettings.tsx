import { useState, useEffect, useRef, useMemo } from 'react';
import { PlusIcon, FolderIcon, CheckIcon, XMarkIcon, CloudArrowDownIcon, MapIcon, ArrowPathIcon, CloudArrowUpIcon, CheckCircleIcon, XCircleIcon, ClockIcon, MagnifyingGlassIcon } from '@heroicons/react/24/outline';
import { toast } from 'sonner';
import { useQueryClient, useQuery } from '@tanstack/react-query';
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
  changeFileDate: string;
  recycleBin: string;
  recycleBinCleanup: number;
  setPermissions: boolean;
  chmodFolder: string;
  chownGroup: string;
}

interface EventMapping {
  id: number;
  sportType: string;
  leagueId?: string;
  leagueName?: string;
  releaseNames: string[];
  isActive: boolean;
  priority: number;
  source: string;
  lastSyncedAt?: string;
}

interface EventMappingSyncResult {
  success: boolean;
  added: number;
  updated: number;
  unchanged: number;
  errors: string[];
  duration: number;
}

interface EventMappingSubmitResult {
  success: boolean;
  requestId?: number;
  message: string;
}

interface MappingRequestStatusUpdate {
  id: number;
  remoteRequestId: number;
  sportType: string;
  leagueName?: string;
  releaseNames: string;
  status: 'approved' | 'rejected';
  reviewNotes?: string;
  reviewedAt?: string;
  submittedAt: string;
}

interface UserLeague {
  id: number;
  name: string;
  sportType: string;
  externalId: string;
  badge?: string;
}

// Sport categories for the dropdown
const SPORT_TYPES = [
  'American Football', 'Athletics', 'Australian Football', 'Badminton', 'Baseball',
  'Basketball', 'Climbing', 'Cricket', 'Cycling', 'Darts', 'Esports', 'Equestrian',
  'Extreme Sports', 'Field Hockey', 'Fighting', 'Gaelic', 'Golf', 'Gymnastics',
  'Handball', 'Ice Hockey', 'Lacrosse', 'Motorsport', 'Multi Sports', 'Netball',
  'Rugby', 'Shooting', 'Skating', 'Skiing', 'Snooker', 'Soccer', 'Table Tennis',
  'Tennis', 'Volleyball', 'Watersports', 'Weightlifting', 'Wintersports'
];

export default function MediaManagementSettings({ showAdvanced: propShowAdvanced = false }: MediaManagementSettingsProps) {
  const queryClient = useQueryClient();
  const [rootFolders, setRootFolders] = useState<RootFolder[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [showAddFolderModal, setShowAddFolderModal] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState<number | null>(null);
  const [newFolderPath, setNewFolderPath] = useState('');
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

  // Event Mappings state (advanced section)
  const [eventMappings, setEventMappings] = useState<EventMapping[]>([]);
  const [eventMappingsLoading, setEventMappingsLoading] = useState(false);
  const [eventMappingsSyncing, setEventMappingsSyncing] = useState(false);
  const [eventMappingsSubmitting, setEventMappingsSubmitting] = useState(false);
  const [lastEventMappingSyncResult, setLastEventMappingSyncResult] = useState<EventMappingSyncResult | null>(null);
  const [showEventMappingSubmitForm, setShowEventMappingSubmitForm] = useState(false);
  const [emSelectedLeague, setEmSelectedLeague] = useState<UserLeague | null>(null);
  const [emLeagueSearch, setEmLeagueSearch] = useState('');
  const [emSportType, setEmSportType] = useState('');
  const [emLeagueName, setEmLeagueName] = useState('');
  const [emReleaseNames, setEmReleaseNames] = useState('');
  const [emReason, setEmReason] = useState('');
  const [emExampleRelease, setEmExampleRelease] = useState('');

  // Check for mapping request status updates (approved/rejected)
  useEffect(() => {
    const checkMappingRequestStatus = async () => {
      try {
        const response = await apiGet('/api/eventmapping/request/status');
        if (!response.ok) return;

        const data = await response.json();
        const updates: MappingRequestStatusUpdate[] = data.updates || [];

        // Show persistent toast for each unnotified status update
        for (const update of updates) {
          const isApproved = update.status === 'approved';
          const title = isApproved ? 'Mapping Request Approved!' : 'Mapping Request Rejected';
          const description = update.leagueName
            ? `Your request for ${update.sportType} / ${update.leagueName} has been ${update.status}.`
            : `Your request for ${update.sportType} has been ${update.status}.`;

          if (isApproved) {
            toast.success(title, {
              description: update.reviewNotes
                ? `${description}\n\nNote: ${update.reviewNotes}`
                : `${description}\n\nThe mapping is now available - sync to get it.`,
              duration: Infinity,
            });
          } else {
            toast.error(title, {
              description: update.reviewNotes
                ? `${description}\n\nReason: ${update.reviewNotes}`
                : description,
              duration: Infinity,
            });
          }

          // Mark as acknowledged so it doesn't show again
          await apiPost(`/api/eventmapping/request/status/${update.id}/acknowledge`, {});
        }
      } catch (error) {
        console.error('Failed to check mapping request status:', error);
      }
    };

    // Check on mount and when advanced settings are shown
    if (showAdvanced) {
      checkMappingRequestStatus();
    }
  }, [showAdvanced]);

  // Fetch user's leagues for the event mapping form
  const { data: userLeagues = [] } = useQuery<UserLeague[]>({
    queryKey: ['leagues-for-mapping'],
    queryFn: async () => {
      const response = await apiGet('/api/leagues');
      if (!response.ok) return [];
      const data = await response.json();
      // API returns: id, name, sport, externalId, logoUrl (camelCase from C# serialization)
      return data
        .filter((league: any) => league.name && league.sport) // Only include valid leagues
        .map((league: any) => ({
          id: league.id,
          name: league.name || '',
          sportType: league.sport || '', // API returns 'sport' not 'sportType'
          externalId: league.externalId || '',
          badge: league.logoUrl || league.posterUrl // API returns 'logoUrl' not 'badge'
        }));
    },
    enabled: showAdvanced,
    staleTime: 5 * 60 * 1000,
  });

  // Filter leagues based on search
  const filteredLeagues = useMemo(() => {
    if (!emLeagueSearch.trim()) return userLeagues;
    const search = emLeagueSearch.toLowerCase();
    return userLeagues.filter(league =>
      (league.name || '').toLowerCase().includes(search) ||
      (league.sportType || '').toLowerCase().includes(search)
    );
  }, [userLeagues, emLeagueSearch]);

  // Fetch all available leagues from TheSportsDB cache for the league name dropdown
  const { data: allLeagues = [] } = useQuery<{ name: string; sport: string }[]>({
    queryKey: ['all-leagues-for-mapping'],
    queryFn: async () => {
      const response = await apiGet('/api/leagues/all');
      if (!response.ok) return [];
      const data = await response.json();
      // API returns TheSportsDB league DTOs with strLeague and strSport
      return data
        .filter((league: any) => league.strLeague && league.strSport)
        .map((league: any) => ({
          name: league.strLeague,
          sport: league.strSport
        }));
    },
    enabled: showAdvanced && showEventMappingSubmitForm,
    staleTime: 10 * 60 * 1000, // Cache for 10 minutes
  });

  // Filter all leagues by selected sport type for the league name dropdown
  const leaguesForSelectedSport = useMemo(() => {
    if (!emSportType) return [];
    return allLeagues
      .filter(league => (league.sport || '').toLowerCase() === emSportType.toLowerCase())
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [allLeagues, emSportType]);

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

  // Load event mappings when advanced settings are shown
  useEffect(() => {
    if (showAdvanced && eventMappings.length === 0 && !eventMappingsLoading) {
      loadEventMappings();
    }
  }, [showAdvanced]);

  const loadEventMappings = async () => {
    setEventMappingsLoading(true);
    try {
      const response = await apiGet('/api/eventmapping');
      if (response.ok) {
        const data = await response.json();
        setEventMappings(data);
      } else {
        console.error('Failed to load event mappings:', response.status);
      }
    } catch (error) {
      console.error('Failed to load event mappings:', error);
    } finally {
      setEventMappingsLoading(false);
    }
  };

  const handleEventMappingSync = async (fullSync: boolean = false) => {
    setEventMappingsSyncing(true);
    try {
      const endpoint = fullSync ? '/api/eventmapping/sync/full' : '/api/eventmapping/sync';
      const response = await apiPost(endpoint, {});

      if (response.ok) {
        const result: EventMappingSyncResult = await response.json();
        setLastEventMappingSyncResult(result);

        if (result.success) {
          toast.success('Sync Complete', {
            description: `Added: ${result.added}, Updated: ${result.updated}, Unchanged: ${result.unchanged}`,
          });
          await loadEventMappings();
        } else {
          toast.error('Sync Issues', {
            description: result.errors.join(', ') || 'Some errors occurred during sync',
          });
        }
      } else {
        const errorText = await response.text();
        toast.error('Sync Failed', {
          description: errorText || 'Failed to sync mappings from server',
        });
      }
    } catch (error) {
      console.error('Sync failed:', error);
      toast.error('Sync Failed', {
        description: error instanceof Error ? error.message : 'An unexpected error occurred',
      });
    } finally {
      setEventMappingsSyncing(false);
    }
  };

  const handleEventMappingSubmitRequest = async () => {
    if (!emSportType.trim()) {
      toast.error('Sport Type Required', { description: 'Please enter a sport type (e.g., Motorsport, Fighting, Basketball)' });
      return;
    }
    if (!emReleaseNames.trim()) {
      toast.error('Release Names Required', { description: 'Please enter at least one release name pattern' });
      return;
    }

    setEventMappingsSubmitting(true);
    try {
      const response = await apiPost('/api/eventmapping/request', {
        sportType: emSportType.trim(),
        leagueName: emLeagueName.trim() || null,
        releaseNames: emReleaseNames.split(',').map(n => n.trim()).filter(n => n),
        reason: emReason.trim() || null,
        exampleRelease: emExampleRelease.trim() || null,
      });

      if (response.ok) {
        const result: EventMappingSubmitResult = await response.json();

        if (result.success) {
          toast.success('Request Submitted', {
            description: result.message,
            duration: 8000,
          });
          setEmSelectedLeague(null);
          setEmLeagueSearch('');
          setEmSportType('');
          setEmLeagueName('');
          setEmReleaseNames('');
          setEmReason('');
          setEmExampleRelease('');
          setShowEventMappingSubmitForm(false);
        } else {
          toast.error('Submission Failed', {
            description: result.message,
          });
        }
      } else {
        const errorText = await response.text();
        toast.error('Submission Failed', {
          description: errorText || 'Failed to submit mapping request',
        });
      }
    } catch (error) {
      console.error('Submit failed:', error);
      toast.error('Submission Failed', {
        description: error instanceof Error ? error.message : 'An unexpected error occurred',
      });
    } finally {
      setEventMappingsSubmitting(false);
    }
  };

  const getEventMappingSourceBadge = (source: string) => {
    switch (source) {
      case 'local':
        return <span className="px-2 py-0.5 bg-blue-600/20 text-blue-400 text-xs rounded">Local Override</span>;
      case 'admin':
        return <span className="px-2 py-0.5 bg-purple-600/20 text-purple-400 text-xs rounded">Official</span>;
      case 'community':
      default:
        return <span className="px-2 py-0.5 bg-green-600/20 text-green-400 text-xs rounded">Community</span>;
    }
  };

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

      if (currentValue && !currentFormat.includes('{Part}')) {
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
      } else if (!currentValue && currentFormat.includes('{Part}')) {
        // DISABLING: Remove {Part} token
        const newFormat = currentFormat.replace('{Part}', '');
        setSettings(prev => ({ ...prev, standardFileFormat: newFormat }));
      }

      // Update the previous value ref
      prevEnableMultiPart.current = currentValue;
    }
  }, [settings.enableMultiPartEpisodes]);

  // Note: In-app navigation blocking would require React Router's unstable_useBlocker
  // For now, we only block browser refresh/close via the useUnsavedChanges hook

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

      {/* Event Mappings (Advanced) */}
      {showAdvanced && (
        <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
          <div className="flex items-center justify-between mb-4">
            <div className="flex items-center">
              <MapIcon className="w-6 h-6 text-red-400 mr-3" />
              <h3 className="text-xl font-semibold text-white">
                Event Mappings
                <span className="ml-2 px-2 py-0.5 bg-yellow-900/30 text-yellow-400 text-xs rounded">
                  Advanced
                </span>
              </h3>
            </div>
          </div>

          {/* Info Banner */}
          <div className="mb-4 p-3 bg-blue-900/20 border border-blue-900/30 rounded-lg">
            <p className="text-blue-300 text-sm">
              <strong>Event Mappings</strong> help Sportarr match release names (like "Formula1", "F1", "UFC") to official
              database names. Mappings sync automatically every 12 hours from the Sportarr API.
            </p>
          </div>

          {/* Sync Controls */}
          <div className="mb-4 p-4 bg-black/30 rounded-lg">
            <div className="flex items-center justify-between mb-3">
              <div>
                <p className="text-white font-medium">Sync Mappings</p>
                <p className="text-gray-400 text-sm">
                  Mappings auto-sync every 12 hours. Manual sync is available below.
                </p>
              </div>
              <div className="flex items-center space-x-2">
                <button
                  onClick={() => handleEventMappingSync(false)}
                  disabled={eventMappingsSyncing}
                  className="px-3 py-1.5 bg-gray-700 hover:bg-gray-600 text-white text-sm rounded-lg transition-colors flex items-center space-x-1.5 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  {eventMappingsSyncing ? (
                    <>
                      <ArrowPathIcon className="w-4 h-4 animate-spin" />
                      <span>Syncing...</span>
                    </>
                  ) : (
                    <>
                      <ArrowPathIcon className="w-4 h-4" />
                      <span>Sync Updates</span>
                    </>
                  )}
                </button>
                <button
                  onClick={() => handleEventMappingSync(true)}
                  disabled={eventMappingsSyncing}
                  className="px-3 py-1.5 bg-red-600 hover:bg-red-700 text-white text-sm rounded-lg transition-colors flex items-center space-x-1.5 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  <ArrowPathIcon className="w-4 h-4" />
                  <span>Full Sync</span>
                </button>
              </div>
            </div>

            {lastEventMappingSyncResult && (
              <div className={`p-2 rounded text-sm ${lastEventMappingSyncResult.success ? 'bg-green-900/20 border border-green-900/30' : 'bg-yellow-900/20 border border-yellow-900/30'}`}>
                <div className="flex items-center space-x-3">
                  {lastEventMappingSyncResult.success ? (
                    <CheckCircleIcon className="w-4 h-4 text-green-400" />
                  ) : (
                    <XCircleIcon className="w-4 h-4 text-yellow-400" />
                  )}
                  <span className={lastEventMappingSyncResult.success ? 'text-green-300' : 'text-yellow-300'}>
                    Added: {lastEventMappingSyncResult.added} | Updated: {lastEventMappingSyncResult.updated} | Unchanged: {lastEventMappingSyncResult.unchanged}
                  </span>
                  <span className="text-gray-500 text-xs">
                    ({(lastEventMappingSyncResult.duration / 1000).toFixed(1)}s)
                  </span>
                </div>
              </div>
            )}
          </div>

          {/* Request New Mapping */}
          <div className="mb-4 p-4 bg-black/30 rounded-lg">
            <div className="flex items-center justify-between mb-2">
              <div className="flex items-center">
                <CloudArrowUpIcon className="w-5 h-5 text-green-400 mr-2" />
                <span className="text-white font-medium">Request New Mapping</span>
              </div>
              {!showEventMappingSubmitForm && (
                <button
                  onClick={() => setShowEventMappingSubmitForm(true)}
                  className="px-3 py-1.5 bg-green-600 hover:bg-green-700 text-white text-sm rounded-lg transition-colors flex items-center space-x-1.5"
                >
                  <PlusIcon className="w-4 h-4" />
                  <span>New Request</span>
                </button>
              )}
            </div>
            <p className="text-gray-400 text-sm">
              Missing a mapping? Submit a request and it will be reviewed by the Sportarr team.
            </p>

            {showEventMappingSubmitForm && (
              <div className="mt-4 grid grid-cols-1 lg:grid-cols-3 gap-4">
                {/* Form Section (2 columns on large screens) */}
                <div className="lg:col-span-2 space-y-3">
                  {/* League Selector */}
                  <div>
                    <label className="block text-white text-sm font-medium mb-1">
                      Select League <span className="text-gray-500">(from your library)</span>
                    </label>
                    {userLeagues.length > 0 ? (
                      <div className="space-y-2">
                        {/* Search Input */}
                        <div className="relative">
                          <MagnifyingGlassIcon className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-500" />
                          <input
                            type="text"
                            value={emLeagueSearch}
                            onChange={(e) => setEmLeagueSearch(e.target.value)}
                            className="w-full pl-9 pr-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white text-sm focus:outline-none focus:border-red-600"
                            placeholder="Search leagues..."
                          />
                        </div>
                        {/* League Grid */}
                        <div className="max-h-48 overflow-y-auto border border-gray-700 rounded-lg bg-gray-900/50">
                          {filteredLeagues.length === 0 ? (
                            <p className="text-gray-500 text-sm p-3 text-center">No leagues found</p>
                          ) : (
                            <div className="divide-y divide-gray-800">
                              {filteredLeagues.map((league) => (
                                <button
                                  key={league.id}
                                  onClick={() => {
                                    setEmSelectedLeague(league);
                                    setEmSportType(league.sportType);
                                    setEmLeagueName(league.name);
                                    setEmLeagueSearch('');
                                  }}
                                  className={`w-full flex items-center gap-2 p-2 text-left transition-colors ${
                                    emSelectedLeague?.id === league.id
                                      ? 'bg-red-600/20 border-l-2 border-l-red-600'
                                      : 'hover:bg-gray-800'
                                  }`}
                                >
                                  {league.badge && (
                                    <img src={league.badge} alt="" className="w-6 h-6 object-contain" />
                                  )}
                                  <div className="flex-1 min-w-0">
                                    <div className="text-white text-sm truncate">{league.name}</div>
                                    <div className="text-gray-500 text-xs">{league.sportType}</div>
                                  </div>
                                  {emSelectedLeague?.id === league.id && (
                                    <CheckIcon className="w-4 h-4 text-red-400 flex-shrink-0" />
                                  )}
                                </button>
                              ))}
                            </div>
                          )}
                        </div>
                        {emSelectedLeague && (
                          <div className="flex items-center gap-2 p-2 bg-red-900/20 border border-red-900/30 rounded-lg">
                            <CheckCircleIcon className="w-4 h-4 text-green-400" />
                            <span className="text-green-300 text-sm">Selected: {emSelectedLeague.name}</span>
                            <button
                              onClick={() => {
                                setEmSelectedLeague(null);
                                setEmSportType('');
                                setEmLeagueName('');
                              }}
                              className="ml-auto text-gray-400 hover:text-white"
                            >
                              <XMarkIcon className="w-4 h-4" />
                            </button>
                          </div>
                        )}
                      </div>
                    ) : (
                      <p className="text-gray-500 text-sm p-2 bg-gray-800 rounded-lg">
                        No leagues in your library. Add leagues first, or manually enter details below.
                      </p>
                    )}
                  </div>

                  {/* Manual Entry / Override */}
                  <div className="pt-2 border-t border-gray-800">
                    <p className="text-gray-500 text-xs mb-2">
                      {emSelectedLeague ? 'Override auto-filled values if needed:' : 'Or enter details manually:'}
                    </p>
                    <div className="grid grid-cols-2 gap-3">
                      <div>
                        <label className="block text-white text-sm font-medium mb-1">
                          Sport Type <span className="text-red-400">*</span>
                        </label>
                        <select
                          value={emSportType}
                          onChange={(e) => {
                            setEmSportType(e.target.value);
                            // Clear league name when sport type changes
                            setEmLeagueName('');
                          }}
                          className="w-full px-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white text-sm focus:outline-none focus:border-red-600"
                        >
                          <option value="">Select sport type...</option>
                          {SPORT_TYPES.map((sport) => (
                            <option key={sport} value={sport}>{sport}</option>
                          ))}
                        </select>
                      </div>
                      <div>
                        <label className="block text-white text-sm font-medium mb-1">League Name</label>
                        <select
                          value={emLeagueName}
                          onChange={(e) => setEmLeagueName(e.target.value)}
                          disabled={!emSportType || leaguesForSelectedSport.length === 0}
                          className="w-full px-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white text-sm focus:outline-none focus:border-red-600 disabled:opacity-50 disabled:cursor-not-allowed"
                        >
                          <option value="">
                            {!emSportType
                              ? "Select sport type first..."
                              : leaguesForSelectedSport.length === 0
                                ? "No leagues found for this sport"
                                : "Select league..."}
                          </option>
                          {leaguesForSelectedSport.map((league) => (
                            <option key={league.name} value={league.name}>{league.name}</option>
                          ))}
                        </select>
                      </div>
                    </div>
                  </div>

                  <div>
                    <label className="block text-white text-sm font-medium mb-1">
                      Release Names <span className="text-red-400">*</span>
                    </label>
                    <input
                      type="text"
                      value={emReleaseNames}
                      onChange={(e) => setEmReleaseNames(e.target.value)}
                      className="w-full px-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white text-sm focus:outline-none focus:border-red-600"
                      placeholder="Comma-separated: Formula1, F1, Formula.1"
                    />
                    <p className="text-gray-500 text-xs mt-1">
                      Common variations of how this league appears in release names
                    </p>
                  </div>

                  <div>
                    <label className="block text-white text-sm font-medium mb-1">Example Release</label>
                    <input
                      type="text"
                      value={emExampleRelease}
                      onChange={(e) => setEmExampleRelease(e.target.value)}
                      className="w-full px-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white text-sm focus:outline-none focus:border-red-600"
                      placeholder="e.g., Formula1.2024.Round.23.Abu.Dhabi.Grand.Prix.Race.1080p"
                    />
                  </div>

                  <div>
                    <label className="block text-white text-sm font-medium mb-1">Reason / Notes</label>
                    <textarea
                      value={emReason}
                      onChange={(e) => setEmReason(e.target.value)}
                      className="w-full px-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white text-sm focus:outline-none focus:border-red-600 h-16 resize-none"
                      placeholder="Why is this mapping needed?"
                    />
                  </div>

                  <div className="flex justify-end space-x-2 pt-2">
                    <button
                      onClick={() => {
                        setShowEventMappingSubmitForm(false);
                        setEmSelectedLeague(null);
                        setEmLeagueSearch('');
                        setEmSportType('');
                        setEmLeagueName('');
                        setEmReleaseNames('');
                        setEmReason('');
                        setEmExampleRelease('');
                      }}
                      className="px-3 py-1.5 bg-gray-700 hover:bg-gray-600 text-white text-sm rounded-lg transition-colors"
                    >
                      Cancel
                    </button>
                    <button
                      onClick={handleEventMappingSubmitRequest}
                      disabled={eventMappingsSubmitting || !emSportType.trim() || !emReleaseNames.trim()}
                      className="px-3 py-1.5 bg-green-600 hover:bg-green-700 text-white text-sm rounded-lg transition-colors flex items-center space-x-1.5 disabled:opacity-50 disabled:cursor-not-allowed"
                    >
                      {eventMappingsSubmitting ? (
                        <>
                          <ArrowPathIcon className="w-4 h-4 animate-spin" />
                          <span>Submitting...</span>
                        </>
                      ) : (
                        <>
                          <CloudArrowUpIcon className="w-4 h-4" />
                          <span>Submit</span>
                        </>
                      )}
                    </button>
                  </div>
                </div>

                {/* Example Section (1 column on large screens) */}
                <div className="lg:col-span-1">
                  <div className="p-3 bg-blue-900/20 border border-blue-900/30 rounded-lg">
                    <h4 className="text-blue-300 text-sm font-medium mb-2 flex items-center">
                      <MapIcon className="w-4 h-4 mr-1.5" />
                      Example Mapping
                    </h4>
                    <div className="space-y-2 text-xs">
                      <div className="p-2 bg-black/30 rounded">
                        <div className="text-gray-400 mb-1">Sport Type:</div>
                        <div className="text-white font-mono">Motorsport</div>
                      </div>
                      <div className="p-2 bg-black/30 rounded">
                        <div className="text-gray-400 mb-1">League Name:</div>
                        <div className="text-white font-mono">Formula 1</div>
                      </div>
                      <div className="p-2 bg-black/30 rounded">
                        <div className="text-gray-400 mb-1">Release Names:</div>
                        <div className="text-white font-mono">Formula1, F1, Formula.1, Formula.One</div>
                      </div>
                      <div className="p-2 bg-black/30 rounded">
                        <div className="text-gray-400 mb-1">Example Release:</div>
                        <div className="text-white font-mono text-[10px] break-all">
                          Formula1.2024.Round.23.Abu.Dhabi.Grand.Prix.Race.1080p.WEB-DL
                        </div>
                      </div>
                    </div>
                    <p className="text-gray-500 text-xs mt-3">
                      Release names are the variations used in torrent/usenet releases.
                      They help Sportarr match releases to the correct league.
                    </p>
                  </div>
                </div>
              </div>
            )}
          </div>

          {/* Current Mappings List */}
          <div className="p-4 bg-black/30 rounded-lg">
            <div className="flex items-center mb-3">
              <MapIcon className="w-5 h-5 text-red-400 mr-2" />
              <span className="text-white font-medium">Current Mappings</span>
              <span className="ml-2 px-2 py-0.5 bg-gray-700 text-gray-300 text-xs rounded">
                {eventMappings.length} total
              </span>
            </div>

            {eventMappingsLoading ? (
              <div className="text-center py-4">
                <ArrowPathIcon className="w-6 h-6 text-gray-500 animate-spin mx-auto mb-2" />
                <p className="text-gray-500 text-sm">Loading mappings...</p>
              </div>
            ) : eventMappings.length === 0 ? (
              <div className="text-center py-4">
                <MapIcon className="w-8 h-8 text-gray-600 mx-auto mb-2" />
                <p className="text-gray-500 text-sm mb-1">No event mappings configured</p>
                <p className="text-gray-600 text-xs">
                  Mappings will sync automatically, or click "Sync Updates" above
                </p>
              </div>
            ) : (
              <div className="overflow-x-auto max-h-64 overflow-y-auto">
                <table className="w-full text-sm">
                  <thead className="sticky top-0 bg-gray-900">
                    <tr className="text-left text-gray-400 text-xs border-b border-gray-800">
                      <th className="pb-2 pr-3">Sport / League</th>
                      <th className="pb-2 pr-3">Release Names</th>
                      <th className="pb-2 pr-3">Source</th>
                      <th className="pb-2 pr-3">Status</th>
                      <th className="pb-2">Last Synced</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-800">
                    {eventMappings.slice(0, 10).map((mapping) => (
                      <tr key={mapping.id} className="text-xs">
                        <td className="py-2 pr-3">
                          <div className="text-white">{mapping.sportType}</div>
                          {mapping.leagueName && (
                            <div className="text-gray-500 text-xs">{mapping.leagueName}</div>
                          )}
                        </td>
                        <td className="py-2 pr-3">
                          <div className="flex flex-wrap gap-1">
                            {mapping.releaseNames.slice(0, 2).map((name, idx) => (
                              <span key={idx} className="px-1.5 py-0.5 bg-gray-800 text-gray-300 text-xs rounded">
                                {name}
                              </span>
                            ))}
                            {mapping.releaseNames.length > 2 && (
                              <span className="px-1.5 py-0.5 bg-gray-700 text-gray-400 text-xs rounded">
                                +{mapping.releaseNames.length - 2}
                              </span>
                            )}
                          </div>
                        </td>
                        <td className="py-2 pr-3">
                          {getEventMappingSourceBadge(mapping.source)}
                        </td>
                        <td className="py-2 pr-3">
                          {mapping.isActive ? (
                            <span className="flex items-center text-green-400">
                              <CheckCircleIcon className="w-3.5 h-3.5 mr-1" />
                              Active
                            </span>
                          ) : (
                            <span className="flex items-center text-gray-500">
                              <XCircleIcon className="w-3.5 h-3.5 mr-1" />
                              Inactive
                            </span>
                          )}
                        </td>
                        <td className="py-2 text-gray-500">
                          {mapping.lastSyncedAt ? (
                            <span className="flex items-center">
                              <ClockIcon className="w-3 h-3 mr-1" />
                              {new Date(mapping.lastSyncedAt).toLocaleDateString()}
                            </span>
                          ) : (
                            <span className="text-gray-600">Never</span>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                {eventMappings.length > 10 && (
                  <p className="text-center text-gray-500 text-xs mt-2">
                    Showing 10 of {eventMappings.length} mappings
                  </p>
                )}
              </div>
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
