import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { toast } from 'sonner';
import {
  ArrowPathIcon,
  CheckCircleIcon,
  ExclamationTriangleIcon,
  XCircleIcon,
  InformationCircleIcon,
  ShieldCheckIcon
} from '@heroicons/react/24/outline';
import apiClient from '../api/client';
import PageHeader from '../components/PageHeader';
import PageShell from '../components/PageShell';

interface HealthCheckResult {
  type: number;
  level: number; // 0=Ok, 1=Notice, 2=Warning, 3=Error
  message: string;
  details?: string;
  checkedAt: string;
  dismissed?: boolean;
}

interface OrphanedEvent {
  id: number;
  title: string;
  eventDate: string;
  leagueId?: number;
  league?: string;
  fileRecordCount: number;
}

const ORPHANED_EVENTS_TYPE = 12;

const healthCheckTypeNames: { [key: number]: string } = {
  0: 'Root Folder',
  1: 'Root Folder Access',
  2: 'Download Client',
  3: 'Indexer',
  4: 'Disk Space',
  5: 'Disk Space Critical',
  6: 'Update Available',
  7: 'Database Migration',
  8: 'Metadata API',
  9: 'Notification Test',
  10: 'Authentication',
  11: 'API Key',
  12: 'Orphaned Events',
  13: 'Database Corruption'
};

const levelNames = ['All OK', 'Notice', 'Warning', 'Error'];
const levelColors = ['text-green-400', 'text-blue-400', 'text-yellow-400', 'text-red-400'];
const levelBgColors = ['bg-green-900/20', 'bg-blue-900/20', 'bg-yellow-900/20', 'bg-red-900/20'];
const levelBorderColors = ['border-green-700/50', 'border-blue-700/50', 'border-yellow-700/50', 'border-red-700/50'];

export default function SystemHealthPage() {
  const [healthChecks, setHealthChecks] = useState<HealthCheckResult[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [lastCheck, setLastCheck] = useState<Date | null>(null);
  const [autoRefresh, setAutoRefresh] = useState(true);
  const [orphanedEvents, setOrphanedEvents] = useState<OrphanedEvent[] | null>(null);
  const [showOrphaned, setShowOrphaned] = useState(false);
  const [fixingOrphaned, setFixingOrphaned] = useState(false);

  const loadOrphanedEvents = async () => {
    try {
      const { data } = await apiClient.get<OrphanedEvent[]>('/system/health/orphaned-events');
      setOrphanedEvents(data);
    } catch (err) {
      console.error('Failed to load orphaned events:', err);
      toast.error('Failed to load orphaned events');
    }
  };

  const toggleOrphaned = async () => {
    const next = !showOrphaned;
    setShowOrphaned(next);
    if (next && orphanedEvents === null) {
      await loadOrphanedEvents();
    }
  };

  // Dismissals hide a Notice/Warning from the header banner (persisted in
  // config, so they survive restarts and updates). The check stays visible
  // here, dimmed, and can be restored. Errors are never dismissible.
  const dismissCheck = async (type: number) => {
    try {
      await apiClient.post('/system/health/dismiss', { type });
      toast.success('Warning dismissed', {
        description: 'It will no longer appear in the header banner. Restore it here anytime.',
      });
      await loadHealthChecks();
    } catch (err) {
      console.error('Failed to dismiss health check:', err);
      toast.error('Failed to dismiss');
    }
  };

  const restoreCheck = async (type: number) => {
    try {
      await apiClient.delete(`/system/health/dismiss/${type}`);
      toast.success('Warning restored to the banner');
      await loadHealthChecks();
    } catch (err) {
      console.error('Failed to restore health check:', err);
      toast.error('Failed to restore');
    }
  };

  const fixOrphaned = async () => {
    setFixingOrphaned(true);
    try {
      const { data } = await apiClient.post<{ repaired: number; cleared: number }>('/system/health/orphaned-events/fix');
      toast.success('Orphaned events fixed', {
        description: `${data.repaired} file path(s) restored, ${data.cleared} event(s) returned to missing.`,
      });
      setOrphanedEvents(null);
      setShowOrphaned(false);
      await loadHealthChecks();
    } catch (err) {
      console.error('Failed to fix orphaned events:', err);
      toast.error('Failed to fix orphaned events');
    } finally {
      setFixingOrphaned(false);
    }
  };

  useEffect(() => {
    loadHealthChecks();

    // Auto-refresh every 60 seconds if enabled
    let interval: number | null = null;
    if (autoRefresh) {
      interval = window.setInterval(loadHealthChecks, 60000);
    }

    return () => {
      if (interval) clearInterval(interval);
    };
  }, [autoRefresh]);

  const loadHealthChecks = async () => {
    try {
      setIsLoading(true);
      const response = await apiClient.get('/system/health');
      setHealthChecks(response.data);
      setLastCheck(new Date());
    } catch (error) {
      console.error('Failed to load health checks:', error);
    } finally {
      setIsLoading(false);
    }
  };

  const getIcon = (level: number) => {
    switch (level) {
      case 0: return <CheckCircleIcon className="w-6 h-6" />;
      case 1: return <InformationCircleIcon className="w-6 h-6" />;
      case 2: return <ExclamationTriangleIcon className="w-6 h-6" />;
      case 3: return <XCircleIcon className="w-6 h-6" />;
      default: return <CheckCircleIcon className="w-6 h-6" />;
    }
  };

  const getOverallHealth = () => {
    if (healthChecks.length === 0) return { level: 0, message: 'Loading...' };

    const maxLevel = Math.max(...healthChecks.map(h => h.level));
    if (maxLevel === 3) return { level: 3, message: 'Critical Issues Detected' };
    if (maxLevel === 2) return { level: 2, message: 'Warnings Present' };
    if (maxLevel === 1) return { level: 1, message: 'Minor Notices' };
    return { level: 0, message: 'All Systems Healthy' };
  };

  const overall = getOverallHealth();
  const errorCount = healthChecks.filter(h => h.level === 3).length;
  const warningCount = healthChecks.filter(h => h.level === 2).length;
  const noticeCount = healthChecks.filter(h => h.level === 1).length;

  return (
    <PageShell>
      <PageHeader
        title="System Health"
        subtitle="Monitor system status and configuration issues"
        actions={
          <>
            <label className="flex items-center gap-2 text-gray-300 text-sm">
              <input
                type="checkbox"
                checked={autoRefresh}
                onChange={(e) => setAutoRefresh(e.target.checked)}
                className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600 focus:ring-offset-gray-900"
              />
              Auto-refresh
            </label>
            <button
              onClick={loadHealthChecks}
              disabled={isLoading}
              className="flex items-center rounded-lg bg-red-600 px-4 py-2 text-white transition-colors hover:bg-red-700 disabled:opacity-50"
            >
              <ArrowPathIcon className={`w-5 h-5 mr-2 ${isLoading ? 'animate-spin' : ''}`} />
              Refresh
            </button>
          </>
        }
      />

      {/* Overall Status Card */}
      <div className={`mb-6 ${levelBgColors[overall.level]} border ${levelBorderColors[overall.level]} rounded-lg p-6`}>
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-4">
              <div className={levelColors[overall.level]}>
                <ShieldCheckIcon className="w-12 h-12" />
              </div>
              <div>
                <h2 className={`text-2xl font-bold ${levelColors[overall.level]}`}>
                  {overall.message}
                </h2>
                {lastCheck && (
                  <p className="text-gray-400 text-sm mt-1">
                    Last checked: {lastCheck.toLocaleTimeString()}
                  </p>
                )}
              </div>
            </div>
            <div className="flex gap-6">
              {errorCount > 0 && (
                <div className="text-center">
                  <div className="text-3xl font-bold text-red-400">{errorCount}</div>
                  <div className="text-sm text-gray-400">Error{errorCount !== 1 ? 's' : ''}</div>
                </div>
              )}
              {warningCount > 0 && (
                <div className="text-center">
                  <div className="text-3xl font-bold text-yellow-400">{warningCount}</div>
                  <div className="text-sm text-gray-400">Warning{warningCount !== 1 ? 's' : ''}</div>
                </div>
              )}
              {noticeCount > 0 && (
                <div className="text-center">
                  <div className="text-3xl font-bold text-blue-400">{noticeCount}</div>
                  <div className="text-sm text-gray-400">Notice{noticeCount !== 1 ? 's' : ''}</div>
                </div>
              )}
              {errorCount === 0 && warningCount === 0 && noticeCount === 0 && (
                <div className="text-center">
                  <div className="text-3xl font-bold text-green-400">✓</div>
                  <div className="text-sm text-gray-400">Healthy</div>
                </div>
              )}
            </div>
          </div>
        </div>

        {/* Health Check Results */}
        {isLoading && healthChecks.length === 0 ? (
          <div className="text-center py-12">
            <div className="inline-block animate-spin rounded-full h-12 w-12 border-4 border-red-600 border-t-transparent"></div>
            <p className="mt-4 text-gray-400">Running health checks...</p>
          </div>
        ) : healthChecks.length === 0 ? (
          <div className="bg-gradient-to-br from-gray-900 to-black border border-gray-700 rounded-lg p-12 text-center">
            <ShieldCheckIcon className="w-16 h-16 text-gray-600 mx-auto mb-4" />
            <p className="text-gray-400">No health check results available</p>
          </div>
        ) : (
          <div className="space-y-3">
            {healthChecks.map((check, index) => (
              <div
                key={index}
                className={`${levelBgColors[check.level]} border ${levelBorderColors[check.level]} rounded-lg p-4 transition-all hover:shadow-lg ${check.dismissed ? 'opacity-50' : ''}`}
              >
                <div className="flex items-start gap-4">
                  <div className={`${levelColors[check.level]} flex-shrink-0 mt-0.5`}>
                    {getIcon(check.level)}
                  </div>
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center justify-between gap-4 mb-2">
                      <h3 className="text-lg font-semibold text-white">{check.message}</h3>
                      <div className="flex items-center gap-3 flex-shrink-0">
                        <span className={`px-3 py-1 ${levelBgColors[check.level]} ${levelColors[check.level]} text-sm font-medium rounded-full border ${levelBorderColors[check.level]}`}>
                          {levelNames[check.level]}
                        </span>
                        <span className="px-3 py-1 bg-gray-800 text-gray-400 text-xs rounded-full">
                          {healthCheckTypeNames[check.type] || 'System'}
                        </span>
                        {check.dismissed ? (
                          <button
                            onClick={() => restoreCheck(check.type)}
                            className="px-3 py-1 bg-gray-800 hover:bg-gray-700 text-gray-300 text-xs rounded-full transition-colors"
                            title="Show this check in the header banner again"
                          >
                            Restore
                          </button>
                        ) : check.level > 0 && check.level < 3 ? (
                          <button
                            onClick={() => dismissCheck(check.type)}
                            className="px-3 py-1 bg-gray-800 hover:bg-gray-700 text-gray-300 text-xs rounded-full transition-colors"
                            title="Hide this check from the header banner. It stays listed here and survives restarts; errors always resurface."
                          >
                            Dismiss
                          </button>
                        ) : null}
                      </div>
                    </div>
                    {check.details && (
                      <p className="text-gray-300 text-sm leading-relaxed">{check.details}</p>
                    )}
                    {check.type === ORPHANED_EVENTS_TYPE && (
                      <div className="mt-3">
                        <div className="flex items-center gap-3">
                          <button
                            onClick={toggleOrphaned}
                            className="px-3 py-1.5 bg-gray-800 hover:bg-gray-700 text-gray-200 text-sm rounded-lg transition-colors"
                          >
                            {showOrphaned ? 'Hide events' : 'Show events'}
                          </button>
                          <button
                            onClick={fixOrphaned}
                            disabled={fixingOrphaned}
                            title="Restores the file path from the event's file records when the file exists on disk, otherwise clears the stale downloaded flag so the event shows as missing again. Never deletes files."
                            className="px-3 py-1.5 bg-red-600 hover:bg-red-700 text-white text-sm rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                          >
                            {fixingOrphaned ? 'Fixing...' : 'Fix all'}
                          </button>
                        </div>
                        {showOrphaned && (
                          <div className="mt-3 space-y-1">
                            {orphanedEvents === null ? (
                              <p className="text-gray-400 text-sm">Loading...</p>
                            ) : orphanedEvents.length === 0 ? (
                              <p className="text-gray-400 text-sm">No orphaned events found. Refresh the health checks.</p>
                            ) : (
                              orphanedEvents.map((evt) => (
                                <div key={evt.id} className="flex items-center justify-between px-3 py-2 bg-black/30 border border-gray-800 rounded-lg text-sm">
                                  <div className="min-w-0">
                                    <span className="text-white">{evt.title}</span>
                                    <span className="text-gray-500 ml-2">
                                      {new Date(evt.eventDate).toLocaleDateString()}
                                      {evt.fileRecordCount > 0
                                        ? ` • ${evt.fileRecordCount} file record(s)`
                                        : ' • no file records'}
                                    </span>
                                  </div>
                                  {evt.leagueId ? (
                                    <Link
                                      to={`/leagues/${evt.leagueId}`}
                                      className="ml-4 text-red-400 hover:text-red-300 flex-shrink-0"
                                    >
                                      {evt.league || 'View league'}
                                    </Link>
                                  ) : (
                                    <span className="ml-4 text-gray-500 flex-shrink-0">{evt.league || 'No league'}</span>
                                  )}
                                </div>
                              ))
                            )}
                          </div>
                        )}
                      </div>
                    )}
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}

        {/* Info Box */}
        <div className="mt-8 bg-gray-900 border border-gray-700 rounded-lg p-6">
          <div className="flex items-start">
            <InformationCircleIcon className="w-5 h-5 text-blue-400 mr-3 mt-0.5 flex-shrink-0" />
            <div className="text-sm text-gray-300">
              <p className="font-semibold text-white mb-2">About Health Checks</p>
              <p className="mb-2">
                Sportarr performs automated health checks to ensure your system is configured correctly
                and operating properly. Health checks run periodically and can be manually refreshed.
              </p>
              <p>
                <strong className="text-white">Errors</strong> indicate critical issues that prevent functionality.{' '}
                <strong className="text-white">Warnings</strong> are issues that should be addressed but don't block operations.{' '}
                <strong className="text-white">Notices</strong> are informational messages about your configuration.
              </p>
            </div>
          </div>
        </div>
    </PageShell>
  );
}
