import { useState, useEffect } from 'react';
import {
  ArrowPathIcon,
  CheckCircleIcon,
  XCircleIcon,
  InformationCircleIcon,
  ArrowDownTrayIcon,
  ChevronDownIcon,
  ChevronRightIcon,
  TrashIcon,
  EyeIcon,
  ClockIcon,
  PlusIcon,
  Cog6ToothIcon,
} from '@heroicons/react/24/outline';
import { toast } from 'sonner';

interface TrashSyncStatus {
  totalSyncedFormats: number;
  customizedFormats: number;
  lastSyncDate: string | null;
  categories: Record<string, number>;
  syncSettings: TrashSyncSettings | null;
}

interface TrashSyncSettings {
  enableAutoSync: boolean;
  autoSyncIntervalHours: number;
  lastAutoSync: string | null;
  autoSyncSportRelevantOnly: boolean;
  autoApplyScoresToProfiles: boolean;
  autoApplyScoreSet: string;
}

interface TrashCustomFormatInfo {
  trashId: string;
  name: string;
  description: string | null;
  category: string | null;
  defaultScore: number | null;
  isSynced: boolean;
  isRecommended: boolean;
}

interface TrashSyncResult {
  success: boolean;
  error: string | null;
  created: number;
  updated: number;
  skipped: number;
  failed: number;
  errors: string[];
  syncedFormats: string[];
  syncedAt: string;
}

interface TrashSyncPreview {
  toCreate: TrashSyncPreviewItem[];
  toUpdate: TrashSyncPreviewItem[];
  toSkip: TrashSyncPreviewItem[];
  totalChanges: number;
}

interface TrashSyncPreviewItem {
  trashId: string;
  name: string;
  category: string | null;
  defaultScore: number | null;
  reason?: string;
  changes?: string[];
}

interface TrashQualityProfileInfo {
  trashId: string;
  name: string;
  description: string | null;
  qualityCount: number;
  formatScoreCount: number;
  minFormatScore: number | null;
  cutoff: string | null;
}

interface ScoreSet {
  [key: string]: string;
}

export default function TrashGuidesSettings() {
  const [status, setStatus] = useState<TrashSyncStatus | null>(null);
  const [availableFormats, setAvailableFormats] = useState<TrashCustomFormatInfo[]>([]);
  const [scoreSets, setScoreSets] = useState<ScoreSet>({});
  const [loading, setLoading] = useState(true);
  const [syncing, setSyncing] = useState(false);
  const [showAllFormats, setShowAllFormats] = useState(false);
  const [selectedFormats, setSelectedFormats] = useState<Set<string>>(new Set());
  const [expandedCategories, setExpandedCategories] = useState<Set<string>>(new Set(['Recommended']));
  const [lastSyncResult, setLastSyncResult] = useState<TrashSyncResult | null>(null);

  // New feature states
  const [showPreviewModal, setShowPreviewModal] = useState(false);
  const [preview, setPreview] = useState<TrashSyncPreview | null>(null);
  const [loadingPreview, setLoadingPreview] = useState(false);
  const [showSettingsModal, setShowSettingsModal] = useState(false);
  const [syncSettings, setSyncSettings] = useState<TrashSyncSettings>({
    enableAutoSync: false,
    autoSyncIntervalHours: 24,
    lastAutoSync: null,
    autoSyncSportRelevantOnly: true,
    autoApplyScoresToProfiles: false,
    autoApplyScoreSet: 'default',
  });
  const [savingSettings, setSavingSettings] = useState(false);
  const [showProfilesModal, setShowProfilesModal] = useState(false);
  const [availableProfiles, setAvailableProfiles] = useState<TrashQualityProfileInfo[]>([]);
  const [loadingProfiles, setLoadingProfiles] = useState(false);
  const [creatingProfile, setCreatingProfile] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);
  const [deleting, setDeleting] = useState(false);

  useEffect(() => {
    loadData();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Reload formats when showAllFormats changes
  useEffect(() => {
    loadFormats();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [showAllFormats]);

  const loadFormats = async () => {
    try {
      const sportRelevantOnly = !showAllFormats;
      const formatsRes = await fetch(`/api/trash/customformats?sportRelevantOnly=${sportRelevantOnly}`);
      if (formatsRes.ok) {
        setAvailableFormats(await formatsRes.json());
      }
    } catch (error) {
      console.error('Error loading formats:', error);
    }
  };

  const loadData = async () => {
    setLoading(true);
    try {
      const sportRelevantOnly = !showAllFormats;
      const [statusRes, formatsRes, scoreSetsRes, settingsRes] = await Promise.all([
        fetch('/api/trash/status'),
        fetch(`/api/trash/customformats?sportRelevantOnly=${sportRelevantOnly}`),
        fetch('/api/trash/scoresets'),
        fetch('/api/trash/settings'),
      ]);

      if (statusRes.ok) {
        setStatus(await statusRes.json());
      }
      if (formatsRes.ok) {
        setAvailableFormats(await formatsRes.json());
      }
      if (scoreSetsRes.ok) {
        setScoreSets(await scoreSetsRes.json());
      }
      if (settingsRes.ok) {
        const settings = await settingsRes.json();
        setSyncSettings(settings);
      }
    } catch (error) {
      console.error('Error loading TRaSH data:', error);
      toast.error('Failed to load TRaSH Guides data');
    } finally {
      setLoading(false);
    }
  };

  const handleSyncAll = async () => {
    setSyncing(true);
    try {
      const response = await fetch('/api/trash/sync', { method: 'POST' });
      const result: TrashSyncResult = await response.json();

      setLastSyncResult(result);

      if (result.success) {
        toast.success('TRaSH Guides Sync Complete', {
          description: `Created: ${result.created}, Updated: ${result.updated}, Skipped: ${result.skipped}`,
        });
        await loadData();
      } else {
        toast.error('Sync Failed', {
          description: result.error || 'Unknown error occurred',
        });
      }
    } catch (error) {
      console.error('Error syncing:', error);
      toast.error('Sync failed', {
        description: 'Failed to connect to TRaSH Guides',
      });
    } finally {
      setSyncing(false);
    }
  };

  const handleSyncSelected = async () => {
    if (selectedFormats.size === 0) {
      toast.error('No formats selected', {
        description: 'Please select at least one custom format to sync',
      });
      return;
    }

    setSyncing(true);
    try {
      const response = await fetch('/api/trash/sync/selected', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(Array.from(selectedFormats)),
      });
      const result: TrashSyncResult = await response.json();

      setLastSyncResult(result);

      if (result.success) {
        toast.success('Selected Formats Synced', {
          description: `Created: ${result.created}, Updated: ${result.updated}`,
        });
        setSelectedFormats(new Set());
        await loadData();
      } else {
        toast.error('Sync Failed', {
          description: result.error || 'Unknown error occurred',
        });
      }
    } catch (error) {
      console.error('Error syncing:', error);
      toast.error('Sync failed');
    } finally {
      setSyncing(false);
    }
  };

  const handlePreviewSync = async () => {
    setLoadingPreview(true);
    setShowPreviewModal(true);
    try {
      const response = await fetch('/api/trash/preview?sportRelevantOnly=true');
      if (response.ok) {
        setPreview(await response.json());
      }
    } catch (error) {
      console.error('Error loading preview:', error);
      toast.error('Failed to load sync preview');
    } finally {
      setLoadingPreview(false);
    }
  };

  const handleDeleteAllSynced = async () => {
    setDeleting(true);
    try {
      const response = await fetch('/api/trash/formats', { method: 'DELETE' });
      const result = await response.json();

      if (result.success) {
        toast.success('Synced Formats Deleted', {
          description: `Removed ${result.updated} custom formats`,
        });
        setShowDeleteConfirm(false);
        await loadData();
      } else {
        toast.error('Delete Failed', { description: result.error });
      }
    } catch (error) {
      console.error('Error deleting:', error);
      toast.error('Failed to delete synced formats');
    } finally {
      setDeleting(false);
    }
  };

  const handleSaveSettings = async () => {
    setSavingSettings(true);
    try {
      const response = await fetch('/api/trash/settings', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(syncSettings),
      });

      if (response.ok) {
        toast.success('Settings Saved');
        setShowSettingsModal(false);
        await loadData();
      } else {
        toast.error('Failed to save settings');
      }
    } catch (error) {
      console.error('Error saving settings:', error);
      toast.error('Failed to save settings');
    } finally {
      setSavingSettings(false);
    }
  };

  const handleLoadProfiles = async () => {
    setLoadingProfiles(true);
    setShowProfilesModal(true);
    try {
      const response = await fetch('/api/trash/profiles');
      if (response.ok) {
        setAvailableProfiles(await response.json());
      }
    } catch (error) {
      console.error('Error loading profiles:', error);
      toast.error('Failed to load profile templates');
    } finally {
      setLoadingProfiles(false);
    }
  };

  const handleCreateProfile = async (trashId: string, name: string) => {
    setCreatingProfile(true);
    try {
      const response = await fetch(`/api/trash/profiles/create?trashId=${encodeURIComponent(trashId)}`, {
        method: 'POST',
      });
      const result = await response.json();

      if (result.success) {
        toast.success('Profile Created', {
          description: `Created "${name}" from TRaSH template`,
        });
        setShowProfilesModal(false);
      } else {
        toast.error('Failed to create profile', { description: result.error });
      }
    } catch (error) {
      console.error('Error creating profile:', error);
      toast.error('Failed to create profile');
    } finally {
      setCreatingProfile(false);
    }
  };

  const toggleCategory = (category: string) => {
    setExpandedCategories((prev) => {
      const next = new Set(prev);
      if (next.has(category)) {
        next.delete(category);
      } else {
        next.add(category);
      }
      return next;
    });
  };

  const toggleFormat = (trashId: string) => {
    setSelectedFormats((prev) => {
      const next = new Set(prev);
      if (next.has(trashId)) {
        next.delete(trashId);
      } else {
        next.add(trashId);
      }
      return next;
    });
  };

  const selectAllInCategory = (category: string) => {
    const categoryFormats = groupedFormats[category] || [];
    setSelectedFormats((prev) => {
      const next = new Set(prev);
      categoryFormats.forEach((f) => {
        if (!f.isSynced) next.add(f.trashId);
      });
      return next;
    });
  };

  const deselectAllInCategory = (category: string) => {
    const categoryFormats = groupedFormats[category] || [];
    setSelectedFormats((prev) => {
      const next = new Set(prev);
      categoryFormats.forEach((f) => next.delete(f.trashId));
      return next;
    });
  };

  // Group formats by category
  const groupedFormats: Record<string, TrashCustomFormatInfo[]> = {};

  // First, add recommended formats
  const recommended = availableFormats.filter((f) => f.isRecommended && !f.isSynced);
  if (recommended.length > 0) {
    groupedFormats['Recommended'] = recommended;
  }

  // Then group by category
  availableFormats.forEach((format) => {
    const category = format.category || 'Other';
    if (!groupedFormats[category]) {
      groupedFormats[category] = [];
    }
    // Avoid duplicates in recommended
    if (!format.isRecommended || format.isSynced) {
      groupedFormats[category].push(format);
    }
  });

  // Sort categories
  const sortedCategories = Object.keys(groupedFormats).sort((a, b) => {
    if (a === 'Recommended') return -1;
    if (b === 'Recommended') return 1;
    return a.localeCompare(b);
  });

  const formatLastSync = (dateString: string | null) => {
    if (!dateString) return 'Never';
    const date = new Date(dateString);
    return date.toLocaleDateString() + ' ' + date.toLocaleTimeString();
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center py-12">
        <ArrowPathIcon className="w-8 h-8 text-blue-500 animate-spin" />
      </div>
    );
  }

  return (
    <div>
      {/* Header - matching other settings pages */}
      <div className="mb-8">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-bold text-white mb-2">TRaSH Guides Integration</h1>
            <p className="text-gray-400">
              Sync custom formats and scores from TRaSH Guides. Sport-relevant formats only.
            </p>
          </div>
          <div className="flex items-center gap-3">
            <button
              onClick={() => setShowSettingsModal(true)}
              className="p-2.5 text-gray-400 hover:text-white hover:bg-gray-700 rounded-lg transition-colors"
              title="Auto-Sync Settings"
            >
              <Cog6ToothIcon className="w-5 h-5" />
            </button>
            <a
              href="https://trash-guides.info/"
              target="_blank"
              rel="noopener noreferrer"
              className="px-4 py-2 text-blue-400 hover:text-blue-300 text-sm flex items-center gap-2 bg-blue-900/20 hover:bg-blue-900/30 rounded-lg transition-colors"
            >
              <InformationCircleIcon className="w-4 h-4" />
              TRaSH Guides
            </a>
          </div>
        </div>
      </div>

      <div className="space-y-6">

      {/* Status Card */}
      <div className="bg-gray-800 rounded-lg p-4 border border-gray-700">
        <div className="flex items-center justify-between mb-3">
          <h3 className="text-lg font-medium text-white">Sync Status</h3>
          {syncSettings.enableAutoSync && (
            <div className="flex items-center gap-1 text-xs text-green-400">
              <ClockIcon className="w-4 h-4" />
              Auto-sync every {syncSettings.autoSyncIntervalHours}h
            </div>
          )}
        </div>
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
          <div>
            <div className="text-2xl font-bold text-blue-400">{status?.totalSyncedFormats || 0}</div>
            <div className="text-sm text-gray-400">Synced Formats</div>
          </div>
          <div>
            <div className="text-2xl font-bold text-yellow-400">{status?.customizedFormats || 0}</div>
            <div className="text-sm text-gray-400">Customized</div>
          </div>
          <div>
            <div className="text-2xl font-bold text-green-400">
              {availableFormats.filter((f) => !f.isSynced).length}
            </div>
            <div className="text-sm text-gray-400">Available to Sync</div>
          </div>
          <div>
            <div className="text-sm text-gray-300">{formatLastSync(status?.lastSyncDate || null)}</div>
            <div className="text-sm text-gray-400">Last Sync</div>
          </div>
        </div>

        {/* Category breakdown */}
        {status?.categories && Object.keys(status.categories).length > 0 && (
          <div className="mt-4 pt-4 border-t border-gray-700">
            <div className="text-sm text-gray-400 mb-2">Synced by Category:</div>
            <div className="flex flex-wrap gap-2">
              {Object.entries(status.categories).map(([category, count]) => (
                <span
                  key={category}
                  className="px-2 py-1 bg-gray-700 rounded text-xs text-gray-300"
                >
                  {category}: {count}
                </span>
              ))}
            </div>
          </div>
        )}
      </div>

      {/* Quick Sync Actions */}
      <div className="bg-gray-800 rounded-lg p-4 border border-gray-700">
        <h3 className="text-lg font-medium text-white mb-3">Quick Actions</h3>
        <div className="flex flex-wrap gap-3">
          <button
            onClick={handleSyncAll}
            disabled={syncing}
            className="flex items-center gap-2 px-4 py-2 bg-blue-600 hover:bg-blue-700 disabled:bg-gray-600 text-white rounded-lg transition-colors"
          >
            {syncing ? (
              <ArrowPathIcon className="w-5 h-5 animate-spin" />
            ) : (
              <ArrowDownTrayIcon className="w-5 h-5" />
            )}
            Sync All Sport-Relevant Formats
          </button>

          {selectedFormats.size > 0 && (
            <button
              onClick={handleSyncSelected}
              disabled={syncing}
              className="flex items-center gap-2 px-4 py-2 bg-green-600 hover:bg-green-700 disabled:bg-gray-600 text-white rounded-lg transition-colors"
            >
              {syncing ? (
                <ArrowPathIcon className="w-5 h-5 animate-spin" />
              ) : (
                <CheckCircleIcon className="w-5 h-5" />
              )}
              Sync Selected ({selectedFormats.size})
            </button>
          )}

          <button
            onClick={handlePreviewSync}
            disabled={syncing}
            className="flex items-center gap-2 px-4 py-2 bg-purple-600 hover:bg-purple-700 disabled:bg-gray-600 text-white rounded-lg transition-colors"
          >
            <EyeIcon className="w-5 h-5" />
            Preview Changes
          </button>

          <button
            onClick={handleLoadProfiles}
            className="flex items-center gap-2 px-4 py-2 bg-indigo-600 hover:bg-indigo-700 text-white rounded-lg transition-colors"
          >
            <PlusIcon className="w-5 h-5" />
            Import Profile Template
          </button>

          <button
            onClick={loadData}
            disabled={loading}
            className="flex items-center gap-2 px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
          >
            <ArrowPathIcon className={`w-5 h-5 ${loading ? 'animate-spin' : ''}`} />
            Refresh
          </button>

          {(status?.totalSyncedFormats || 0) > 0 && (
            <button
              onClick={() => setShowDeleteConfirm(true)}
              className="flex items-center gap-2 px-4 py-2 bg-red-600/20 hover:bg-red-600/40 text-red-400 rounded-lg transition-colors"
            >
              <TrashIcon className="w-5 h-5" />
              Delete All Synced
            </button>
          )}
        </div>
      </div>

      {/* Last Sync Result */}
      {lastSyncResult && (
        <div
          className={`bg-gray-800 rounded-lg p-4 border ${
            lastSyncResult.success ? 'border-green-600' : 'border-red-600'
          }`}
        >
          <div className="flex items-center gap-2 mb-2">
            {lastSyncResult.success ? (
              <CheckCircleIcon className="w-5 h-5 text-green-500" />
            ) : (
              <XCircleIcon className="w-5 h-5 text-red-500" />
            )}
            <h3 className="text-lg font-medium text-white">
              {lastSyncResult.success ? 'Sync Completed' : 'Sync Failed'}
            </h3>
          </div>

          {lastSyncResult.success ? (
            <div className="grid grid-cols-4 gap-4 text-sm">
              <div>
                <span className="text-green-400 font-bold">{lastSyncResult.created}</span>
                <span className="text-gray-400 ml-1">created</span>
              </div>
              <div>
                <span className="text-blue-400 font-bold">{lastSyncResult.updated}</span>
                <span className="text-gray-400 ml-1">updated</span>
              </div>
              <div>
                <span className="text-yellow-400 font-bold">{lastSyncResult.skipped}</span>
                <span className="text-gray-400 ml-1">skipped</span>
              </div>
              <div>
                <span className="text-red-400 font-bold">{lastSyncResult.failed}</span>
                <span className="text-gray-400 ml-1">failed</span>
              </div>
            </div>
          ) : (
            <p className="text-red-400 text-sm">{lastSyncResult.error}</p>
          )}

          {lastSyncResult.errors.length > 0 && (
            <div className="mt-2 text-xs text-red-400">
              {lastSyncResult.errors.slice(0, 5).map((err, i) => (
                <div key={i}>{err}</div>
              ))}
              {lastSyncResult.errors.length > 5 && (
                <div>...and {lastSyncResult.errors.length - 5} more errors</div>
              )}
            </div>
          )}
        </div>
      )}

      {/* Available Formats */}
      <div className="bg-gray-800 rounded-lg border border-gray-700">
        <div className="p-4 border-b border-gray-700">
          <div className="flex items-center justify-between">
            <h3 className="text-lg font-medium text-white">Available Custom Formats</h3>
            <button
              onClick={() => setShowAllFormats(!showAllFormats)}
              className="text-sm text-blue-400 hover:text-blue-300"
            >
              {showAllFormats ? 'Show Sport-Relevant Only' : 'Show All Formats'}
            </button>
          </div>
          <p className="text-sm text-gray-400 mt-1">
            Select custom formats to sync. Already synced formats are marked with a checkmark.
          </p>
        </div>

        <div className="divide-y divide-gray-700">
          {sortedCategories.map((category) => {
            const formats = groupedFormats[category];
            const isExpanded = expandedCategories.has(category);
            const syncedCount = formats.filter((f) => f.isSynced).length;
            const selectedCount = formats.filter((f) => selectedFormats.has(f.trashId)).length;

            return (
              <div key={category}>
                <button
                  onClick={() => toggleCategory(category)}
                  className="w-full flex items-center justify-between p-3 hover:bg-gray-700/50 transition-colors"
                >
                  <div className="flex items-center gap-2">
                    {isExpanded ? (
                      <ChevronDownIcon className="w-4 h-4 text-gray-400" />
                    ) : (
                      <ChevronRightIcon className="w-4 h-4 text-gray-400" />
                    )}
                    <span
                      className={`font-medium ${
                        category === 'Recommended' ? 'text-green-400' : 'text-white'
                      }`}
                    >
                      {category}
                    </span>
                    <span className="text-sm text-gray-500">
                      ({formats.length} formats, {syncedCount} synced)
                    </span>
                  </div>
                  <div className="flex items-center gap-2">
                    {selectedCount > 0 && (
                      <span className="text-xs bg-blue-600 text-white px-2 py-0.5 rounded">
                        {selectedCount} selected
                      </span>
                    )}
                    {isExpanded && (
                      <div className="flex gap-1" onClick={(e) => e.stopPropagation()}>
                        <button
                          onClick={() => selectAllInCategory(category)}
                          className="text-xs text-blue-400 hover:text-blue-300 px-2"
                        >
                          Select All
                        </button>
                        <button
                          onClick={() => deselectAllInCategory(category)}
                          className="text-xs text-gray-400 hover:text-gray-300 px-2"
                        >
                          Deselect
                        </button>
                      </div>
                    )}
                  </div>
                </button>

                {isExpanded && (
                  <div className="bg-gray-900/50 px-4 py-2 grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-2">
                    {formats.map((format) => (
                      <div
                        key={format.trashId}
                        className={`flex items-center gap-2 p-2 rounded ${
                          format.isSynced
                            ? 'bg-green-900/20 border border-green-800/50'
                            : selectedFormats.has(format.trashId)
                            ? 'bg-blue-900/30 border border-blue-700'
                            : 'bg-gray-800 border border-gray-700'
                        }`}
                      >
                        {format.isSynced ? (
                          <CheckCircleIcon className="w-5 h-5 text-green-500 flex-shrink-0" />
                        ) : (
                          <input
                            type="checkbox"
                            checked={selectedFormats.has(format.trashId)}
                            onChange={() => toggleFormat(format.trashId)}
                            className="w-4 h-4 rounded border-gray-600 bg-gray-700 text-blue-600 focus:ring-blue-500"
                          />
                        )}
                        <div className="flex-1 min-w-0">
                          <div className="flex items-center gap-2">
                            <span className="text-sm text-white truncate">{format.name}</span>
                            {format.isRecommended && !format.isSynced && (
                              <span className="text-[10px] bg-green-600/50 text-green-300 px-1 rounded">
                                REC
                              </span>
                            )}
                          </div>
                          {format.defaultScore !== null && format.defaultScore !== 0 && (
                            <span
                              className={`text-xs ${
                                format.defaultScore > 0 ? 'text-green-400' : 'text-red-400'
                              }`}
                            >
                              Score: {format.defaultScore > 0 ? '+' : ''}
                              {format.defaultScore}
                            </span>
                          )}
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            );
          })}
        </div>
      </div>

      {/* Info Box */}
      <div className="bg-blue-900/20 border border-blue-800 rounded-lg p-4">
        <div className="flex gap-3">
          <InformationCircleIcon className="w-6 h-6 text-blue-400 flex-shrink-0" />
          <div className="text-sm text-blue-200">
            <p className="font-medium mb-1">About TRaSH Guides Integration</p>
            <ul className="list-disc list-inside space-y-1 text-blue-300">
              <li>
                Custom formats are filtered to only show sport-relevant options (audio, video quality,
                streaming services, languages)
              </li>
              <li>Anime-specific formats are excluded</li>
              <li>
                Synced formats can be customized - they won't be overwritten on the next sync unless
                you reset them
              </li>
              <li>
                Use "Apply TRaSH Scores" in Quality Profiles to set recommended scores for all synced
                formats
              </li>
              <li>
                Enable auto-sync in settings to keep your custom formats updated automatically
              </li>
            </ul>
          </div>
        </div>
      </div>
      </div>

      {/* Preview Modal */}
      {showPreviewModal && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-gray-900 border border-gray-700 rounded-lg max-w-2xl w-full max-h-[80vh] overflow-hidden flex flex-col">
            <div className="p-4 border-b border-gray-700 flex items-center justify-between">
              <h3 className="text-lg font-semibold text-white">Sync Preview</h3>
              <button
                onClick={() => setShowPreviewModal(false)}
                className="text-gray-400 hover:text-white"
              >
                <XCircleIcon className="w-6 h-6" />
              </button>
            </div>

            <div className="p-4 overflow-y-auto flex-1">
              {loadingPreview ? (
                <div className="flex items-center justify-center py-8">
                  <ArrowPathIcon className="w-8 h-8 text-blue-500 animate-spin" />
                </div>
              ) : preview ? (
                <div className="space-y-4">
                  {preview.toCreate.length > 0 && (
                    <div>
                      <h4 className="text-green-400 font-medium mb-2">
                        Will Create ({preview.toCreate.length})
                      </h4>
                      <div className="space-y-1">
                        {preview.toCreate.map((item) => (
                          <div key={item.trashId} className="text-sm text-gray-300 flex items-center gap-2">
                            <span className="text-green-400">+</span>
                            <span>{item.name}</span>
                            {item.defaultScore !== null && item.defaultScore !== 0 && (
                              <span className={`text-xs ${item.defaultScore > 0 ? 'text-green-400' : 'text-red-400'}`}>
                                ({item.defaultScore > 0 ? '+' : ''}{item.defaultScore})
                              </span>
                            )}
                          </div>
                        ))}
                      </div>
                    </div>
                  )}

                  {preview.toUpdate.length > 0 && (
                    <div>
                      <h4 className="text-blue-400 font-medium mb-2">
                        Will Update ({preview.toUpdate.length})
                      </h4>
                      <div className="space-y-1">
                        {preview.toUpdate.map((item) => (
                          <div key={item.trashId} className="text-sm">
                            <div className="text-gray-300 flex items-center gap-2">
                              <span className="text-blue-400">~</span>
                              <span>{item.name}</span>
                            </div>
                            {item.changes && item.changes.length > 0 && (
                              <div className="ml-4 text-xs text-gray-500">
                                {item.changes.join(', ')}
                              </div>
                            )}
                          </div>
                        ))}
                      </div>
                    </div>
                  )}

                  {preview.toSkip.length > 0 && (
                    <div>
                      <h4 className="text-yellow-400 font-medium mb-2">
                        Will Skip ({preview.toSkip.length})
                      </h4>
                      <div className="space-y-1">
                        {preview.toSkip.map((item) => (
                          <div key={item.trashId} className="text-sm text-gray-500 flex items-center gap-2">
                            <span className="text-yellow-400">-</span>
                            <span>{item.name}</span>
                            {item.reason && <span className="text-xs">({item.reason})</span>}
                          </div>
                        ))}
                      </div>
                    </div>
                  )}

                  {preview.totalChanges === 0 && preview.toSkip.length === 0 && (
                    <p className="text-gray-400 text-center py-4">No changes to sync</p>
                  )}
                </div>
              ) : (
                <p className="text-gray-400">Failed to load preview</p>
              )}
            </div>

            <div className="p-4 border-t border-gray-700 flex justify-end gap-2">
              <button
                onClick={() => setShowPreviewModal(false)}
                className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg"
              >
                Close
              </button>
              {preview && preview.totalChanges > 0 && (
                <button
                  onClick={() => {
                    setShowPreviewModal(false);
                    handleSyncAll();
                  }}
                  className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg"
                >
                  Sync Now
                </button>
              )}
            </div>
          </div>
        </div>
      )}

      {/* Settings Modal */}
      {showSettingsModal && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-gray-900 border border-gray-700 rounded-lg max-w-lg w-full max-h-[90vh] overflow-y-auto">
            <div className="p-4 border-b border-gray-700 flex items-center justify-between sticky top-0 bg-gray-900">
              <h3 className="text-lg font-semibold text-white">Auto-Sync Settings</h3>
              <button
                onClick={() => setShowSettingsModal(false)}
                className="text-gray-400 hover:text-white"
              >
                <XCircleIcon className="w-6 h-6" />
              </button>
            </div>

            <div className="p-4 space-y-5">
              {/* Enable Auto-Sync */}
              <div className="p-4 bg-gray-800/50 rounded-lg border border-gray-700">
                <label className="flex items-start gap-3 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={syncSettings.enableAutoSync}
                    onChange={(e) => setSyncSettings({ ...syncSettings, enableAutoSync: e.target.checked })}
                    className="w-5 h-5 mt-0.5 rounded border-gray-600 bg-gray-700 text-blue-600"
                  />
                  <div>
                    <span className="text-white font-medium">Enable Auto-Sync</span>
                    <p className="text-sm text-gray-400 mt-1">
                      Automatically download and update custom formats from TRaSH Guides on a schedule.
                      This keeps your formats up-to-date with the latest regex patterns and scoring recommendations.
                    </p>
                  </div>
                </label>
              </div>

              {syncSettings.enableAutoSync && (
                <>
                  {/* Sync Interval */}
                  <div className="p-4 bg-gray-800/50 rounded-lg border border-gray-700">
                    <label className="block text-sm font-medium text-white mb-2">
                      Sync Interval (hours)
                    </label>
                    <input
                      type="number"
                      min={1}
                      max={168}
                      value={syncSettings.autoSyncIntervalHours}
                      onChange={(e) =>
                        setSyncSettings({ ...syncSettings, autoSyncIntervalHours: parseInt(e.target.value) || 24 })
                      }
                      className="w-full px-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white"
                    />
                    <p className="text-xs text-gray-500 mt-2">
                      How often to check for updates. TRaSH Guides typically updates a few times per week,
                      so 24-48 hours is usually sufficient.
                    </p>
                  </div>

                  {/* Auto-Apply Scores */}
                  <div className="p-4 bg-gray-800/50 rounded-lg border border-gray-700">
                    <label className="flex items-start gap-3 cursor-pointer">
                      <input
                        type="checkbox"
                        checked={syncSettings.autoApplyScoresToProfiles}
                        onChange={(e) =>
                          setSyncSettings({ ...syncSettings, autoApplyScoresToProfiles: e.target.checked })
                        }
                        className="w-5 h-5 mt-0.5 rounded border-gray-600 bg-gray-700 text-blue-600"
                      />
                      <div>
                        <span className="text-white font-medium">Auto-Apply Scores to TRaSH Profiles</span>
                        <p className="text-sm text-gray-400 mt-1">
                          After syncing custom formats, automatically update the scores in quality profiles
                          that were imported from TRaSH Guides.
                        </p>
                        <p className="text-xs text-green-500/80 mt-2">
                          ✓ Only affects profiles created via "Import Profile Template" - your custom profiles are safe.
                        </p>
                      </div>
                    </label>
                  </div>

                  {/* Score Set Selection */}
                  {syncSettings.autoApplyScoresToProfiles && (
                    <div className="p-4 bg-gray-800/50 rounded-lg border border-gray-700">
                      <label className="block text-sm font-medium text-white mb-2">Score Set</label>
                      <select
                        value={syncSettings.autoApplyScoreSet}
                        onChange={(e) => setSyncSettings({ ...syncSettings, autoApplyScoreSet: e.target.value })}
                        className="w-full px-3 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white"
                      >
                        {Object.entries(scoreSets).map(([key, name]) => (
                          <option key={key} value={key}>
                            {name}
                          </option>
                        ))}
                      </select>
                      <div className="mt-3 p-3 bg-blue-900/20 border border-blue-800/50 rounded-lg">
                        <p className="text-sm text-blue-300 font-medium mb-2">What is a Score Set?</p>
                        <p className="text-xs text-blue-200/80">
                          TRaSH Guides provides different scoring presets optimized for different languages and preferences:
                        </p>
                        <ul className="text-xs text-blue-200/70 mt-2 space-y-1 ml-3 list-disc">
                          <li><strong>Default</strong> - Standard English-focused scoring. Best for most users.</li>
                          <li><strong>French (Multi-Audio)</strong> - Prioritizes releases with French + original audio tracks.</li>
                          <li><strong>French (VOSTFR)</strong> - Prioritizes original audio with French subtitles.</li>
                          <li><strong>German / German (Multi)</strong> - Optimized for German language releases.</li>
                        </ul>
                        <p className="text-xs text-blue-200/60 mt-2">
                          Each set adjusts scores for language-related custom formats to prefer your chosen language configuration.
                        </p>
                      </div>
                    </div>
                  )}
                </>
              )}
            </div>

            <div className="p-4 border-t border-gray-700 flex justify-end gap-2">
              <button
                onClick={() => setShowSettingsModal(false)}
                className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg"
              >
                Cancel
              </button>
              <button
                onClick={handleSaveSettings}
                disabled={savingSettings}
                className="px-4 py-2 bg-blue-600 hover:bg-blue-700 disabled:bg-gray-600 text-white rounded-lg"
              >
                {savingSettings ? 'Saving...' : 'Save Settings'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Profile Templates Modal */}
      {showProfilesModal && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-gray-900 border border-gray-700 rounded-lg max-w-2xl w-full max-h-[80vh] overflow-hidden flex flex-col">
            <div className="p-4 border-b border-gray-700 flex items-center justify-between">
              <h3 className="text-lg font-semibold text-white">TRaSH Quality Profile Templates</h3>
              <button
                onClick={() => setShowProfilesModal(false)}
                className="text-gray-400 hover:text-white"
              >
                <XCircleIcon className="w-6 h-6" />
              </button>
            </div>

            <div className="p-4 overflow-y-auto flex-1">
              {loadingProfiles ? (
                <div className="flex items-center justify-center py-8">
                  <ArrowPathIcon className="w-8 h-8 text-blue-500 animate-spin" />
                </div>
              ) : availableProfiles.length > 0 ? (
                <div className="space-y-3">
                  {availableProfiles.map((profile) => (
                    <div
                      key={profile.trashId}
                      className="bg-gray-800 border border-gray-700 rounded-lg p-4"
                    >
                      <div className="flex items-start justify-between">
                        <div>
                          <h4 className="text-white font-medium">{profile.name}</h4>
                          {profile.description && (
                            <p className="text-sm text-gray-400 mt-1">{profile.description}</p>
                          )}
                          <div className="flex gap-4 mt-2 text-xs text-gray-500">
                            <span>{profile.qualityCount} qualities</span>
                            <span>{profile.formatScoreCount} format scores</span>
                            {profile.minFormatScore !== null && (
                              <span>Min score: {profile.minFormatScore}</span>
                            )}
                            {profile.cutoff && <span>Cutoff: {profile.cutoff}</span>}
                          </div>
                        </div>
                        <button
                          onClick={() => handleCreateProfile(profile.trashId, profile.name)}
                          disabled={creatingProfile}
                          className="px-3 py-1.5 bg-green-600 hover:bg-green-700 disabled:bg-gray-600 text-white text-sm rounded-lg"
                        >
                          {creatingProfile ? 'Creating...' : 'Create'}
                        </button>
                      </div>
                    </div>
                  ))}
                </div>
              ) : (
                <p className="text-gray-400 text-center py-4">No profile templates available</p>
              )}
            </div>

            <div className="p-4 border-t border-gray-700">
              <p className="text-sm text-gray-400">
                Note: Profile templates require synced custom formats to apply scores correctly.
                Sync custom formats first for best results.
              </p>
            </div>
          </div>
        </div>
      )}

      {/* Delete Confirmation Modal */}
      {showDeleteConfirm && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-gray-900 border border-red-600 rounded-lg max-w-md w-full p-6">
            <h3 className="text-lg font-semibold text-white mb-2">Delete All Synced Formats?</h3>
            <p className="text-gray-400 mb-4">
              This will remove all {status?.totalSyncedFormats || 0} custom formats that were synced from
              TRaSH Guides. This action cannot be undone.
            </p>
            <div className="flex justify-end gap-2">
              <button
                onClick={() => setShowDeleteConfirm(false)}
                className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg"
              >
                Cancel
              </button>
              <button
                onClick={handleDeleteAllSynced}
                disabled={deleting}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 disabled:bg-gray-600 text-white rounded-lg"
              >
                {deleting ? 'Deleting...' : 'Delete All'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
