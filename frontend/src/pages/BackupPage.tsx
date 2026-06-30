import React, { useState, useEffect } from 'react';
import { toast } from 'sonner';
import {
  ArrowDownTrayIcon,
  ArrowUpTrayIcon,
  TrashIcon,
  ExclamationTriangleIcon,
  InformationCircleIcon
} from '@heroicons/react/24/outline';
import PageHeader from '../components/PageHeader';
import PageShell from '../components/PageShell';
import { apiGet, apiPost, apiDelete } from '../utils/api';
import { createRequestUrl, getImageUrl } from '../utils/request';
import { parseAsUtc } from '../utils/timezone';

interface BackupInfo {
  name: string;
  path: string;
  size: number;
  sizeFormatted: string;
  createdAt: string;
}

interface RestoreReport {
  id: number;
  backupFileName: string;
  startedAt: string;
  completedAt?: string | null;
  totalEventFiles: number;
  filesFound: number;
  filesMissing: number;
  filesSkippedUnreachableRoot: number;
  pathRemapsJson?: string | null;
  sourceHost?: string | null;
  sourceSportarrVersion?: string | null;
  status: string;
  notes?: string | null;
}

interface PathRemapPreview {
  missingFileCount: number;
  oldPrefix?: string | null;
  newPrefix?: string | null;
  sampleSize: number;
  sampleMatches: number;
  affectedRowCount: number;
  notes: string;
  hasSuggestion: boolean;
}

interface LibraryRescanResult {
  startedAt: string;
  completedAt?: string | null;
  rootsScanned: number;
  totalFilesScanned: number;
  matchedFiles: number;
  unmatchedFiles: number;
  alreadyInLibraryFiles: number;
  autoImported: number;
  importFailures: number;
  importSkipped: number;
  unreachableRoots: string[];
  notes: string;
}

const BackupPage: React.FC = () => {
  const [backups, setBackups] = useState<BackupInfo[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [restoring, setRestoring] = useState(false);
  const [showRestoreConfirm, setShowRestoreConfirm] = useState<string | null>(null);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState<string | null>(null);
  const [backupNote, setBackupNote] = useState('');

  // Phase 1-3 state. Restore reports drive the post-restore reconciliation
  // panel; the remap preview drives the "we think your paths drifted -- want
  // us to fix them?" wizard; rescanResult shows the outcome of a full
  // library rescan triggered from the maintenance card.
  const [restoreReports, setRestoreReports] = useState<RestoreReport[]>([]);
  const [remapPreview, setRemapPreview] = useState<PathRemapPreview | null>(null);
  const [remapBusy, setRemapBusy] = useState(false);
  const [rescanBusy, setRescanBusy] = useState(false);
  const [rescanResult, setRescanResult] = useState<LibraryRescanResult | null>(null);
  const [uploading, setUploading] = useState(false);

  useEffect(() => {
    fetchBackups();
    fetchRestoreReports();
  }, []);

  // Pull the most useful message out of an error response. The
  // backend's Results.Problem returns ProblemDetails JSON with
  // { title, detail, status } — surfacing detail (or title) gives
  // the user something actionable like "Backup folder not writable"
  // instead of the previous generic "Failed to fetch backups".
  const extractError = async (response: Response, fallback: string): Promise<string> => {
    try {
      const data = await response.json();
      if (typeof data === 'object' && data) {
        if (typeof data.detail === 'string' && data.detail.length > 0) return data.detail;
        if (typeof data.title === 'string' && data.title.length > 0) return data.title;
        if (typeof data.error === 'string' && data.error.length > 0) return data.error;
        if (typeof data.message === 'string' && data.message.length > 0) return data.message;
      }
    } catch { /* not JSON, ignore */ }
    return `${fallback} (HTTP ${response.status})`;
  };

  const fetchBackups = async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await apiGet('/api/system/backup');
      if (!response.ok) throw new Error(await extractError(response, 'Failed to fetch backups'));
      const data: BackupInfo[] = await response.json();
      setBackups(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'An error occurred');
    } finally {
      setLoading(false);
    }
  };

  const handleCreateBackup = async () => {
    setCreating(true);
    setError(null);
    try {
      const url = backupNote
        ? `/api/system/backup?note=${encodeURIComponent(backupNote)}`
        : '/api/system/backup';

      const response = await apiPost(url, {});
      if (!response.ok) {
        const message = await extractError(response, 'Failed to create backup');
        throw new Error(message);
      }

      setBackupNote('');
      await fetchBackups();
      toast.success('Backup Created', {
        description: 'Backup created successfully!',
      });
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Failed to create backup';
      setError(message);
      toast.error('Backup Failed', { description: message });
    } finally {
      setCreating(false);
    }
  };

  const handleRestoreBackup = async (backupName: string) => {
    setRestoring(true);
    setError(null);
    try {
      const response = await apiPost(`/api/system/backup/restore/${encodeURIComponent(backupName)}`, {});
      if (!response.ok) throw new Error('Failed to restore backup');

      const result = await response.json();
      toast.success('Backup Restored', {
        description: result.message || 'Backup restored. Reconciliation running.',
        duration: 10000,
      });
      setShowRestoreConfirm(null);
      // Refresh the report list so the new entry appears immediately.
      // The orchestrator updates the same row out of band as the disk
      // scan completes, so a brief delayed re-fetch picks up the final
      // counts. We do not block on it: the user can come back later.
      fetchRestoreReports();
      setTimeout(fetchRestoreReports, 5_000);
      setTimeout(fetchRestoreReports, 30_000);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to restore backup');
    } finally {
      setRestoring(false);
    }
  };

  const fetchRestoreReports = async () => {
    try {
      const response = await apiGet('/api/system/restore-reports');
      if (!response.ok) return;
      const data = await response.json();
      if (Array.isArray(data)) setRestoreReports(data);
    } catch {
      /* non-fatal; the panel just stays empty */
    }
  };

  // Path-remap wizard. Detect inspects every missing EventFile and
  // computes a candidate prefix rewrite that resolves them against the
  // configured root folders. Apply executes the rewrite atomically.
  // Both are read-only by default; the apply step is gated on the
  // confirm button.
  const handleDetectRemap = async () => {
    setRemapBusy(true);
    setError(null);
    try {
      const response = await apiGet('/api/library/remap/preview');
      if (!response.ok) throw new Error('Failed to detect path drift');
      const preview = await response.json();
      setRemapPreview(preview);
      if (!preview.hasSuggestion) {
        toast.info('No path drift detected', { description: preview.notes });
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to detect path drift');
    } finally {
      setRemapBusy(false);
    }
  };

  const handleApplyRemap = async () => {
    if (!remapPreview || !remapPreview.hasSuggestion) return;
    setRemapBusy(true);
    setError(null);
    try {
      const response = await apiPost('/api/library/remap/apply', {
        from: remapPreview.oldPrefix,
        to: remapPreview.newPrefix,
      });
      if (!response.ok) throw new Error('Failed to apply remap');
      const result = await response.json();
      toast.success('Paths remapped', {
        description: `Rewrote ${result.affected} EventFile rows. Disk scan triggered.`,
      });
      setRemapPreview(null);
      fetchRestoreReports();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to apply remap');
    } finally {
      setRemapBusy(false);
    }
  };

  const handleLibraryRescan = async () => {
    setRescanBusy(true);
    setError(null);
    setRescanResult(null);
    try {
      const response = await apiPost('/api/library/rescan', {});
      if (!response.ok) throw new Error('Failed to rescan library');
      const result = await response.json();
      setRescanResult(result);
      toast.success('Library rescan complete', {
        description: result.notes,
        duration: 8000,
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to rescan library');
    } finally {
      setRescanBusy(false);
    }
  };

  const handleBackupUpload = async (file: File) => {
    setUploading(true);
    setError(null);
    try {
      const form = new FormData();
      form.append('backup', file);
      const response = await fetch(createRequestUrl('/api/system/backup/upload'), {
        method: 'POST',
        body: form,
      });
      if (!response.ok) throw new Error('Upload failed');
      toast.success('Backup uploaded', {
        description: 'You can now restore from it like any other backup below.',
      });
      await fetchBackups();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to upload backup');
    } finally {
      setUploading(false);
    }
  };

  const handleDeleteBackup = async (backupName: string) => {
    setError(null);
    try {
      const response = await apiDelete(`/api/system/backup/${encodeURIComponent(backupName)}`);
      if (!response.ok) throw new Error('Failed to delete backup');

      await fetchBackups();
      setShowDeleteConfirm(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete backup');
    }
  };

  const handleDownloadBackup = async (backupName: string) => {
    try {
      const response = await apiGet(`/api/system/backup/download/${encodeURIComponent(backupName)}`);
      if (!response.ok) throw new Error('Failed to download backup');
      const blob = await response.blob();
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = backupName;
      document.body.appendChild(a);
      a.click();
      window.URL.revokeObjectURL(url);
      document.body.removeChild(a);
    } catch (err) {
      toast.error('Download Failed', {
        description: err instanceof Error ? err.message : 'Failed to download backup',
      });
    }
  };

  const handleCleanupOldBackups = async () => {
    if (!confirm('This will delete backups older than the configured retention period. Continue?')) {
      return;
    }

    setError(null);
    try {
      const response = await apiPost('/api/system/backup/cleanup', {});
      if (!response.ok) throw new Error('Failed to cleanup old backups');

      const result = await response.json();
      toast.success('Cleanup Complete', {
        description: result.message || 'Old backups cleaned up successfully.',
      });
      await fetchBackups();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to cleanup backups');
    }
  };

  const formatDate = (dateString: string) => {
    const date = parseAsUtc(dateString);
    return date.toLocaleString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  };

  return (
    <PageShell>
      <PageHeader
        title="Backup"
        subtitle="Create and manage database backups for disaster recovery"
      />

      {/* Info Box */}
      <div className="mb-6 p-4 bg-blue-900/20 border border-blue-800 rounded-lg">
        <div className="flex items-start gap-3">
          <InformationCircleIcon className="w-5 h-5 text-blue-400 flex-shrink-0 mt-0.5" />
          <div className="text-sm text-gray-300">
            <strong className="text-white">About Backups:</strong> Backups are stored as ZIP files containing your Sportarr database.
            Configure backup folder and retention settings in Media Management settings.
            After restoring a backup, you must restart Sportarr for changes to take effect.
          </div>
        </div>
      </div>

      {/* Create Backup Section */}
      <div className="mb-6 bg-gray-800 rounded-lg p-6 border border-gray-700">
        <h2 className="text-xl font-semibold text-white mb-4">Create New Backup</h2>
        <div className="flex gap-3">
          <input
            type="text"
            placeholder="Optional note for this backup..."
            value={backupNote}
            onChange={(e) => setBackupNote(e.target.value)}
            className="flex-1 px-4 py-2 bg-gray-700 border border-gray-600 rounded text-white placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
          <button
            onClick={handleCreateBackup}
            disabled={creating}
            className="px-6 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2"
          >
            <ArrowDownTrayIcon className="w-5 h-5" />
            {creating ? 'Creating...' : 'Backup Now'}
          </button>
          <button
            onClick={handleCleanupOldBackups}
            className="px-4 py-2 bg-gray-700 text-white rounded hover:bg-gray-600 flex items-center gap-2"
          >
            <TrashIcon className="w-5 h-5" />
            Cleanup Old
          </button>
        </div>
      </div>

      {/* Error Display */}
      {error && (
        <div className="mb-6 p-4 bg-red-900/20 border border-red-800 rounded-lg">
          <p className="text-red-400">Error: {error}</p>
        </div>
      )}

      {/* Backups List */}
      <div className="bg-gray-800 rounded-lg border border-gray-700">
        <div className="p-4 border-b border-gray-700">
          <h2 className="text-xl font-semibold text-white">Available Backups</h2>
        </div>

        {loading ? (
          <div className="flex items-center justify-center py-12">
            <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-400"></div>
            <span className="ml-3 text-gray-400">Loading backups...</span>
          </div>
        ) : backups.length === 0 ? (
          <div className="text-center py-12 text-gray-400">
            <ArrowDownTrayIcon className="w-12 h-12 mx-auto mb-3 opacity-50" />
            <p>No backups found. Create your first backup above.</p>
          </div>
        ) : (
          <div className="divide-y divide-gray-700">
            {backups.map((backup) => (
              <div key={backup.name} className="p-4 hover:bg-gray-750 transition-colors">
                <div className="flex items-center justify-between">
                  <div className="flex-1">
                    <h3 className="text-white font-medium mb-1">{backup.name}</h3>
                    <div className="flex items-center gap-4 text-sm text-gray-400">
                      <span>{formatDate(backup.createdAt)}</span>
                      <span className="text-gray-600">•</span>
                      <span>{backup.sizeFormatted}</span>
                    </div>
                  </div>
                  <div className="flex gap-2">
                    <button
                      onClick={() => handleDownloadBackup(backup.name)}
                      className="px-4 py-2 bg-blue-900/30 text-blue-400 rounded hover:bg-blue-900/50 flex items-center gap-2 transition-colors"
                    >
                      <ArrowDownTrayIcon className="w-4 h-4" />
                      Download
                    </button>
                    <button
                      onClick={() => setShowRestoreConfirm(backup.name)}
                      disabled={restoring}
                      className="px-4 py-2 bg-green-900/30 text-green-400 rounded hover:bg-green-900/50 disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2 transition-colors"
                    >
                      <ArrowUpTrayIcon className="w-4 h-4" />
                      Restore
                    </button>
                    <button
                      onClick={() => setShowDeleteConfirm(backup.name)}
                      className="px-4 py-2 bg-red-900/30 text-red-400 rounded hover:bg-red-900/50 flex items-center gap-2 transition-colors"
                    >
                      <TrashIcon className="w-4 h-4" />
                      Delete
                    </button>
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Reconciliation + library maintenance. Surfaces what post-restore
          reconciliation found, lets the admin remap path prefixes when
          paths drifted between source and target hosts, and exposes the
          rescan-library button for catching files that exist on disk
          but aren't in the EventFile table. Sits below the backup list
          because it's the next thing an admin reaches for after a
          restore. */}
      <div className="bg-gray-900 rounded-lg border border-gray-700 p-6 mt-6">
        <h2 className="text-xl font-semibold text-white mb-2">
          Reconciliation &amp; library maintenance
        </h2>
        <p className="text-sm text-gray-400 mb-4">
          After a backup restore, the disk scanner verifies every file path
          against on-disk reality. Use the tools below to fix path drift
          (when the backup came from a machine with different storage
          paths) and to import media files that exist on disk but aren't
          yet linked to events.
        </p>

        {/* Upload from another machine */}
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mb-6">
          <div className="bg-gray-800 rounded p-4 border border-gray-700">
            <h3 className="text-sm font-semibold text-white mb-1">Upload backup from another machine</h3>
            <p className="text-xs text-gray-400 mb-3">
              Drop in a backup zip from another Sportarr instance. It
              lands in the backups folder and shows up in the list above.
            </p>
            <input
              type="file"
              accept=".zip,application/zip"
              disabled={uploading}
              onChange={(e) => {
                const f = e.target.files?.[0];
                if (f) handleBackupUpload(f);
                e.target.value = '';
              }}
              className="block w-full text-sm text-gray-300 file:mr-3 file:py-1.5 file:px-3 file:rounded file:border-0 file:text-sm file:font-medium file:bg-blue-600 file:text-white hover:file:bg-blue-500"
            />
            {uploading && (
              <p className="mt-2 text-xs text-blue-400">Uploading...</p>
            )}
          </div>

          {/* Library rescan */}
          <div className="bg-gray-800 rounded p-4 border border-gray-700">
            <h3 className="text-sm font-semibold text-white mb-1">Rescan library</h3>
            <p className="text-xs text-gray-400 mb-3">
              Walk every configured root folder and auto-import media
              files that match known events. Medium-confidence matches
              land in Library Import for review.
            </p>
            <button
              type="button"
              onClick={handleLibraryRescan}
              disabled={rescanBusy}
              className="px-3 py-1.5 text-sm bg-blue-600 text-white rounded hover:bg-blue-500 disabled:opacity-50"
            >
              {rescanBusy ? 'Rescanning...' : 'Rescan library'}
            </button>
            {rescanResult && (
              <div className="mt-3 text-xs text-gray-300 space-y-0.5">
                <div>Roots scanned: <span className="text-white">{rescanResult.rootsScanned}</span></div>
                <div>Files scanned: <span className="text-white">{rescanResult.totalFilesScanned}</span></div>
                <div>Matched: <span className="text-green-400">{rescanResult.matchedFiles}</span></div>
                <div>Auto-imported: <span className="text-green-400">{rescanResult.autoImported}</span></div>
                <div>Unmatched: <span className="text-yellow-400">{rescanResult.unmatchedFiles}</span></div>
                {rescanResult.unreachableRoots.length > 0 && (
                  <div className="text-red-400">Unreachable roots: {rescanResult.unreachableRoots.join(', ')}</div>
                )}
              </div>
            )}
          </div>
        </div>

        {/* Path remap */}
        <div className="bg-gray-800 rounded p-4 border border-gray-700 mb-6">
          <h3 className="text-sm font-semibold text-white mb-1">Path remap</h3>
          <p className="text-xs text-gray-400 mb-3">
            When a backup was produced on a host with different storage
            paths, EventFile rows still point at the old paths. Detect
            scans the missing-file set and suggests a single prefix
            rewrite that resolves them under your configured root folders.
          </p>
          <button
            type="button"
            onClick={handleDetectRemap}
            disabled={remapBusy}
            className="px-3 py-1.5 text-sm bg-blue-600 text-white rounded hover:bg-blue-500 disabled:opacity-50"
          >
            {remapBusy ? 'Detecting...' : 'Detect path drift'}
          </button>
          {remapPreview && (
            <div className="mt-3 text-xs text-gray-300 space-y-1">
              <div>Missing files: <span className="text-white">{remapPreview.missingFileCount}</span></div>
              {remapPreview.hasSuggestion ? (
                <>
                  <div>Old prefix: <span className="font-mono text-yellow-400">{remapPreview.oldPrefix}</span></div>
                  <div>New prefix: <span className="font-mono text-green-400">{remapPreview.newPrefix}</span></div>
                  <div>Sample resolution: {remapPreview.sampleMatches}/{remapPreview.sampleSize}</div>
                  <div>Will rewrite: <span className="text-white">{remapPreview.affectedRowCount}</span> EventFile rows</div>
                  <button
                    type="button"
                    onClick={handleApplyRemap}
                    disabled={remapBusy}
                    className="mt-2 px-3 py-1.5 text-sm bg-green-600 text-white rounded hover:bg-green-500 disabled:opacity-50"
                  >
                    Apply remap
                  </button>
                </>
              ) : (
                <div className="text-gray-400">{remapPreview.notes}</div>
              )}
            </div>
          )}
        </div>

        {/* Restore reports */}
        <div className="bg-gray-800 rounded p-4 border border-gray-700">
          <h3 className="text-sm font-semibold text-white mb-2">Recent restore reports</h3>
          {restoreReports.length === 0 ? (
            <p className="text-xs text-gray-400">
              No reports yet. After you restore a backup, the
              reconciliation outcome shows up here.
            </p>
          ) : (
            <div className="space-y-2">
              {restoreReports.slice(0, 5).map((r) => (
                <div key={r.id} className="text-xs text-gray-300 bg-gray-900 rounded p-3 border border-gray-700">
                  <div className="flex justify-between mb-1">
                    <span className="font-mono text-white truncate" title={r.backupFileName}>{r.backupFileName}</span>
                    <span className={
                      r.status === 'completed' ? 'text-green-400' :
                      r.status === 'failed' ? 'text-red-400' : 'text-yellow-400'
                    }>{r.status}</span>
                  </div>
                  <div className="grid grid-cols-2 md:grid-cols-4 gap-2">
                    <div>Total files: <span className="text-white">{r.totalEventFiles}</span></div>
                    <div>Found: <span className="text-green-400">{r.filesFound}</span></div>
                    <div>Missing: <span className="text-yellow-400">{r.filesMissing}</span></div>
                    <div>Skipped: <span className="text-gray-400">{r.filesSkippedUnreachableRoot}</span></div>
                  </div>
                  {r.sourceHost && (
                    <div className="mt-1 text-gray-400">
                      From host <span className="text-white">{r.sourceHost}</span>
                      {r.sourceSportarrVersion && (<> running v{r.sourceSportarrVersion}</>)}
                    </div>
                  )}
                  {r.notes && (
                    <div className="mt-1 text-yellow-400 whitespace-pre-wrap">{r.notes}</div>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>
      </div>

      {/* Restore Confirmation Modal */}
      {showRestoreConfirm && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div className="bg-gray-800 rounded-lg p-6 max-w-md w-full mx-4 border border-gray-700">
            <div className="flex items-start gap-3 mb-4">
              <ExclamationTriangleIcon className="w-6 h-6 text-yellow-400 flex-shrink-0" />
              <div>
                <h3 className="text-lg font-semibold text-white mb-2">Restore Backup?</h3>
                <p className="text-sm text-gray-300 mb-2">
                  This will replace your current database with the backup:
                </p>
                <p className="text-sm text-white font-mono bg-gray-900 p-2 rounded mb-2">
                  {showRestoreConfirm}
                </p>
                <p className="text-sm text-yellow-400">
                  ⚠️ Your current database will be backed up before restoration, but you will need to restart Sportarr after the restore.
                </p>
              </div>
            </div>
            <div className="flex gap-3 justify-end">
              <button
                onClick={() => setShowRestoreConfirm(null)}
                className="px-4 py-2 bg-gray-700 text-white rounded hover:bg-gray-600"
              >
                Cancel
              </button>
              <button
                onClick={() => handleRestoreBackup(showRestoreConfirm)}
                disabled={restoring}
                className="px-4 py-2 bg-green-600 text-white rounded hover:bg-green-700 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                {restoring ? 'Restoring...' : 'Restore'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Delete Confirmation Modal */}
      {showDeleteConfirm && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div className="bg-gray-800 rounded-lg p-6 max-w-md w-full mx-4 border border-gray-700">
            <div className="flex items-start gap-3 mb-4">
              <TrashIcon className="w-6 h-6 text-red-400 flex-shrink-0" />
              <div>
                <h3 className="text-lg font-semibold text-white mb-2">Delete Backup?</h3>
                <p className="text-sm text-gray-300 mb-2">
                  Are you sure you want to delete this backup?
                </p>
                <p className="text-sm text-white font-mono bg-gray-900 p-2 rounded">
                  {showDeleteConfirm}
                </p>
              </div>
            </div>
            <div className="flex gap-3 justify-end">
              <button
                onClick={() => setShowDeleteConfirm(null)}
                className="px-4 py-2 bg-gray-700 text-white rounded hover:bg-gray-600"
              >
                Cancel
              </button>
              <button
                onClick={() => handleDeleteBackup(showDeleteConfirm)}
                className="px-4 py-2 bg-red-600 text-white rounded hover:bg-red-700"
              >
                Delete
              </button>
            </div>
          </div>
        </div>
      )}
    </PageShell>
  );
};

export default BackupPage;
