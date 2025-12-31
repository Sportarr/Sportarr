import { useState, useEffect } from 'react';
import { MapIcon, ArrowPathIcon, PlusIcon, CloudArrowUpIcon, CheckCircleIcon, XCircleIcon, ClockIcon } from '@heroicons/react/24/outline';
import { toast } from 'sonner';
import { apiGet, apiPost } from '../../utils/api';
import SettingsHeader from '../../components/SettingsHeader';

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

interface SyncResult {
  success: boolean;
  added: number;
  updated: number;
  unchanged: number;
  errors: string[];
  duration: number;
}

interface SubmitResult {
  success: boolean;
  requestId?: number;
  message: string;
}

export default function EventMappingsSettings() {
  const [loading, setLoading] = useState(true);
  const [syncing, setSyncing] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [mappings, setMappings] = useState<EventMapping[]>([]);
  const [lastSyncResult, setLastSyncResult] = useState<SyncResult | null>(null);
  const [showSubmitForm, setShowSubmitForm] = useState(false);

  // Submit form state
  const [sportType, setSportType] = useState('');
  const [leagueName, setLeagueName] = useState('');
  const [releaseNames, setReleaseNames] = useState('');
  const [reason, setReason] = useState('');
  const [exampleRelease, setExampleRelease] = useState('');

  useEffect(() => {
    loadMappings();
  }, []);

  const loadMappings = async () => {
    try {
      const response = await apiGet('/api/eventmapping');
      if (response.ok) {
        const data = await response.json();
        setMappings(data);
      } else {
        console.error('Failed to load mappings:', response.status);
      }
    } catch (error) {
      console.error('Failed to load mappings:', error);
    } finally {
      setLoading(false);
    }
  };

  const handleSync = async (fullSync: boolean = false) => {
    setSyncing(true);
    try {
      const endpoint = fullSync ? '/api/eventmapping/sync/full' : '/api/eventmapping/sync';
      const response = await apiPost(endpoint, {});

      if (response.ok) {
        const result: SyncResult = await response.json();
        setLastSyncResult(result);

        if (result.success) {
          toast.success('Sync Complete', {
            description: `Added: ${result.added}, Updated: ${result.updated}, Unchanged: ${result.unchanged}`,
          });
          // Reload mappings to show updated data
          await loadMappings();
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
      setSyncing(false);
    }
  };

  const handleSubmitRequest = async () => {
    // Validate required fields
    if (!sportType.trim()) {
      toast.error('Sport Type Required', { description: 'Please enter a sport type (e.g., Motorsport, Fighting, Basketball)' });
      return;
    }
    if (!releaseNames.trim()) {
      toast.error('Release Names Required', { description: 'Please enter at least one release name pattern' });
      return;
    }

    setSubmitting(true);
    try {
      const response = await apiPost('/api/eventmapping/request', {
        sportType: sportType.trim(),
        leagueName: leagueName.trim() || null,
        releaseNames: releaseNames.split(',').map(n => n.trim()).filter(n => n),
        reason: reason.trim() || null,
        exampleRelease: exampleRelease.trim() || null,
      });

      if (response.ok) {
        const result: SubmitResult = await response.json();

        if (result.success) {
          toast.success('Request Submitted', {
            description: result.message,
            duration: 8000,
          });
          // Reset form
          setSportType('');
          setLeagueName('');
          setReleaseNames('');
          setReason('');
          setExampleRelease('');
          setShowSubmitForm(false);
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
      setSubmitting(false);
    }
  };

  const getSourceBadge = (source: string) => {
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

  if (loading) {
    return (
      <div className="max-w-4xl mx-auto">
        <div className="mb-8">
          <h2 className="text-3xl font-bold text-white mb-2">Event Mappings</h2>
          <p className="text-gray-400">Configure release name to event mappings</p>
        </div>
        <div className="text-center py-12">
          <p className="text-gray-500">Loading mappings...</p>
        </div>
      </div>
    );
  }

  return (
    <div>
      <SettingsHeader
        title="Event Mappings"
        subtitle="Map release naming patterns to official sports event names"
        showSaveButton={false}
      />

      <div className="max-w-4xl mx-auto px-6">
        {/* Info Banner */}
        <div className="mb-6 p-4 bg-blue-900/20 border border-blue-900/30 rounded-lg">
          <p className="text-blue-300 text-sm">
            <strong>Event Mappings</strong> help Sportarr match release names (like "Formula1", "F1", "UFC") to official
            database names. Mappings are synced from the community-maintained Sportarr API and can be supplemented with local overrides.
          </p>
        </div>

        {/* Sync Section */}
        <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
          <div className="flex items-center justify-between mb-4">
            <div className="flex items-center">
              <ArrowPathIcon className="w-6 h-6 text-red-400 mr-3" />
              <h3 className="text-xl font-semibold text-white">Sync Mappings</h3>
            </div>
            <div className="flex items-center space-x-3">
              <button
                onClick={() => handleSync(false)}
                disabled={syncing}
                className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors flex items-center space-x-2 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                {syncing ? (
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
                onClick={() => handleSync(true)}
                disabled={syncing}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors flex items-center space-x-2 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                <ArrowPathIcon className="w-4 h-4" />
                <span>Full Sync</span>
              </button>
            </div>
          </div>

          <p className="text-gray-400 text-sm mb-4">
            Sync event mappings from the central Sportarr API. "Sync Updates" fetches only new/changed mappings since last sync.
            "Full Sync" replaces all community mappings (local overrides are preserved).
          </p>

          {lastSyncResult && (
            <div className={`p-3 rounded-lg text-sm ${lastSyncResult.success ? 'bg-green-900/20 border border-green-900/30' : 'bg-yellow-900/20 border border-yellow-900/30'}`}>
              <div className="flex items-center space-x-4">
                {lastSyncResult.success ? (
                  <CheckCircleIcon className="w-5 h-5 text-green-400" />
                ) : (
                  <XCircleIcon className="w-5 h-5 text-yellow-400" />
                )}
                <span className={lastSyncResult.success ? 'text-green-300' : 'text-yellow-300'}>
                  Added: {lastSyncResult.added} | Updated: {lastSyncResult.updated} | Unchanged: {lastSyncResult.unchanged}
                </span>
                <span className="text-gray-500">
                  ({(lastSyncResult.duration / 1000).toFixed(1)}s)
                </span>
              </div>
              {lastSyncResult.errors.length > 0 && (
                <div className="mt-2 text-yellow-400 text-xs">
                  {lastSyncResult.errors.join(', ')}
                </div>
              )}
            </div>
          )}
        </div>

        {/* Submit Request Section */}
        <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
          <div className="flex items-center justify-between mb-4">
            <div className="flex items-center">
              <CloudArrowUpIcon className="w-6 h-6 text-red-400 mr-3" />
              <h3 className="text-xl font-semibold text-white">Request New Mapping</h3>
            </div>
            {!showSubmitForm && (
              <button
                onClick={() => setShowSubmitForm(true)}
                className="px-4 py-2 bg-green-600 hover:bg-green-700 text-white rounded-lg transition-colors flex items-center space-x-2"
              >
                <PlusIcon className="w-4 h-4" />
                <span>New Request</span>
              </button>
            )}
          </div>

          <p className="text-gray-400 text-sm mb-4">
            Missing a mapping for a sport or league? Submit a request to add it to the community database.
            Requests are reviewed and added to help everyone in the Sportarr community.
          </p>

          {showSubmitForm && (
            <div className="mt-4 p-4 bg-black/30 rounded-lg space-y-4">
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-white font-medium mb-2">
                    Sport Type <span className="text-red-400">*</span>
                  </label>
                  <input
                    type="text"
                    value={sportType}
                    onChange={(e) => setSportType(e.target.value)}
                    className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    placeholder="e.g., Motorsport, Fighting, Basketball"
                  />
                </div>
                <div>
                  <label className="block text-white font-medium mb-2">League Name</label>
                  <input
                    type="text"
                    value={leagueName}
                    onChange={(e) => setLeagueName(e.target.value)}
                    className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    placeholder="e.g., Formula 1, UFC, NBA"
                  />
                </div>
              </div>

              <div>
                <label className="block text-white font-medium mb-2">
                  Release Names <span className="text-red-400">*</span>
                </label>
                <input
                  type="text"
                  value={releaseNames}
                  onChange={(e) => setReleaseNames(e.target.value)}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                  placeholder="Comma-separated: Formula1, F1, Formula.1, Formula.One"
                />
                <p className="text-xs text-gray-500 mt-1">
                  Enter the naming patterns used in releases, separated by commas
                </p>
              </div>

              <div>
                <label className="block text-white font-medium mb-2">Example Release</label>
                <input
                  type="text"
                  value={exampleRelease}
                  onChange={(e) => setExampleRelease(e.target.value)}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                  placeholder="e.g., Formula1.2024.Round.23.Abu.Dhabi.Grand.Prix.Race.1080p"
                />
                <p className="text-xs text-gray-500 mt-1">
                  Provide an example release name to help reviewers understand the naming pattern
                </p>
              </div>

              <div>
                <label className="block text-white font-medium mb-2">Reason / Notes</label>
                <textarea
                  value={reason}
                  onChange={(e) => setReason(e.target.value)}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600 h-20 resize-none"
                  placeholder="Why is this mapping needed? Any additional context?"
                />
              </div>

              <div className="flex justify-end space-x-3">
                <button
                  onClick={() => {
                    setShowSubmitForm(false);
                    setSportType('');
                    setLeagueName('');
                    setReleaseNames('');
                    setReason('');
                    setExampleRelease('');
                  }}
                  className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
                >
                  Cancel
                </button>
                <button
                  onClick={handleSubmitRequest}
                  disabled={submitting || !sportType.trim() || !releaseNames.trim()}
                  className="px-4 py-2 bg-green-600 hover:bg-green-700 text-white rounded-lg transition-colors flex items-center space-x-2 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  {submitting ? (
                    <>
                      <ArrowPathIcon className="w-4 h-4 animate-spin" />
                      <span>Submitting...</span>
                    </>
                  ) : (
                    <>
                      <CloudArrowUpIcon className="w-4 h-4" />
                      <span>Submit Request</span>
                    </>
                  )}
                </button>
              </div>
            </div>
          )}
        </div>

        {/* Current Mappings List */}
        <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
          <div className="flex items-center mb-4">
            <MapIcon className="w-6 h-6 text-red-400 mr-3" />
            <h3 className="text-xl font-semibold text-white">Current Mappings</h3>
            <span className="ml-3 px-2 py-0.5 bg-gray-700 text-gray-300 text-xs rounded">
              {mappings.length} total
            </span>
          </div>

          {mappings.length === 0 ? (
            <div className="text-center py-8">
              <MapIcon className="w-12 h-12 text-gray-600 mx-auto mb-3" />
              <p className="text-gray-500 mb-2">No event mappings configured</p>
              <p className="text-gray-600 text-sm">
                Click "Sync Updates" above to fetch community mappings from the Sportarr API
              </p>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full">
                <thead>
                  <tr className="text-left text-gray-400 text-sm border-b border-gray-800">
                    <th className="pb-3 pr-4">Sport / League</th>
                    <th className="pb-3 pr-4">Release Names</th>
                    <th className="pb-3 pr-4">Source</th>
                    <th className="pb-3 pr-4">Status</th>
                    <th className="pb-3">Last Synced</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-800">
                  {mappings.map((mapping) => (
                    <tr key={mapping.id} className="text-sm">
                      <td className="py-3 pr-4">
                        <div className="text-white font-medium">{mapping.sportType}</div>
                        {mapping.leagueName && (
                          <div className="text-gray-500 text-xs">{mapping.leagueName}</div>
                        )}
                      </td>
                      <td className="py-3 pr-4">
                        <div className="flex flex-wrap gap-1">
                          {mapping.releaseNames.slice(0, 3).map((name, idx) => (
                            <span key={idx} className="px-2 py-0.5 bg-gray-800 text-gray-300 text-xs rounded">
                              {name}
                            </span>
                          ))}
                          {mapping.releaseNames.length > 3 && (
                            <span className="px-2 py-0.5 bg-gray-700 text-gray-400 text-xs rounded">
                              +{mapping.releaseNames.length - 3} more
                            </span>
                          )}
                        </div>
                      </td>
                      <td className="py-3 pr-4">
                        {getSourceBadge(mapping.source)}
                      </td>
                      <td className="py-3 pr-4">
                        {mapping.isActive ? (
                          <span className="flex items-center text-green-400 text-xs">
                            <CheckCircleIcon className="w-4 h-4 mr-1" />
                            Active
                          </span>
                        ) : (
                          <span className="flex items-center text-gray-500 text-xs">
                            <XCircleIcon className="w-4 h-4 mr-1" />
                            Inactive
                          </span>
                        )}
                      </td>
                      <td className="py-3 text-gray-500 text-xs">
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
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
