import { useState, useEffect } from 'react';
import {
  ArrowPathIcon,
  CheckCircleIcon,
  XCircleIcon,
  InformationCircleIcon,
  ArrowDownTrayIcon,
  ChevronDownIcon,
  ChevronRightIcon,
} from '@heroicons/react/24/outline';
import { toast } from 'sonner';

interface TrashSyncStatus {
  totalSyncedFormats: number;
  customizedFormats: number;
  lastSyncDate: string | null;
  categories: Record<string, number>;
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

  useEffect(() => {
    loadData();
  }, []);

  const loadData = async () => {
    setLoading(true);
    try {
      const [statusRes, formatsRes, scoreSetsRes] = await Promise.all([
        fetch('/api/trash/status'),
        fetch('/api/trash/customformats?sportRelevantOnly=true'),
        fetch('/api/trash/scoresets'),
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
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-xl font-semibold text-white">TRaSH Guides Integration</h2>
          <p className="text-sm text-gray-400 mt-1">
            Sync custom formats and scores from TRaSH Guides. Sport-relevant formats only.
          </p>
        </div>
        <a
          href="https://trash-guides.info/"
          target="_blank"
          rel="noopener noreferrer"
          className="text-blue-400 hover:text-blue-300 text-sm flex items-center gap-1"
        >
          <InformationCircleIcon className="w-4 h-4" />
          TRaSH Guides
        </a>
      </div>

      {/* Status Card */}
      <div className="bg-gray-800 rounded-lg p-4 border border-gray-700">
        <h3 className="text-lg font-medium text-white mb-3">Sync Status</h3>
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
            onClick={loadData}
            disabled={loading}
            className="flex items-center gap-2 px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
          >
            <ArrowPathIcon className={`w-5 h-5 ${loading ? 'animate-spin' : ''}`} />
            Refresh
          </button>
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
            </ul>
          </div>
        </div>
      </div>
    </div>
  );
}
