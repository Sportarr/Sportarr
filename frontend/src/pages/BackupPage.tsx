import React, { useState, useEffect } from 'react';
import { toast } from 'sonner';
import {
  ArrowDownTrayIcon,
  ArrowUpTrayIcon,
  TrashIcon,
  ExclamationTriangleIcon,
  InformationCircleIcon
} from '@heroicons/react/24/outline';

interface BackupInfo {
  name: string;
  path: string;
  size: number;
  sizeFormatted: string;
  createdAt: string;
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

  useEffect(() => {
    fetchBackups();
  }, []);

  const fetchBackups = async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await fetch('/api/system/backup');
      if (!response.ok) throw new Error('Failed to fetch backups');
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

      const response = await fetch(url, { method: 'POST' });
      if (!response.ok) throw new Error('Failed to create backup');

      setBackupNote('');
      await fetchBackups();
      toast.success('Backup Created', {
        description: 'Backup created successfully!',
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create backup');
    } finally {
      setCreating(false);
    }
  };

  const handleRestoreBackup = async (backupName: string) => {
    setRestoring(true);
    setError(null);
    try {
      const response = await fetch(`/api/system/backup/restore/${encodeURIComponent(backupName)}`, {
        method: 'POST'
      });
      if (!response.ok) throw new Error('Failed to restore backup');

      const result = await response.json();
      toast.success('Backup Restored', {
        description: result.message || 'Backup restored successfully! Please restart Fightarr.',
        duration: 10000,
      });
      setShowRestoreConfirm(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to restore backup');
    } finally {
      setRestoring(false);
    }
  };

  const handleDeleteBackup = async (backupName: string) => {
    setError(null);
    try {
      const response = await fetch(`/api/system/backup/${encodeURIComponent(backupName)}`, {
        method: 'DELETE'
      });
      if (!response.ok) throw new Error('Failed to delete backup');

      await fetchBackups();
      setShowDeleteConfirm(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete backup');
    }
  };

  const handleCleanupOldBackups = async () => {
    if (!confirm('This will delete backups older than the configured retention period. Continue?')) {
      return;
    }

    setError(null);
    try {
      const response = await fetch('/api/system/backup/cleanup', { method: 'POST' });
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
    const date = new Date(dateString);
    return date.toLocaleString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  };

  return (
    <div className="p-6">
      <div className="mb-6">
        <h1 className="text-3xl font-bold text-white mb-2">Backup</h1>
        <p className="text-gray-400">
          Create and manage database backups for disaster recovery
        </p>
      </div>

      {/* Info Box */}
      <div className="mb-6 p-4 bg-blue-900/20 border border-blue-800 rounded-lg">
        <div className="flex items-start gap-3">
          <InformationCircleIcon className="w-5 h-5 text-blue-400 flex-shrink-0 mt-0.5" />
          <div className="text-sm text-gray-300">
            <strong className="text-white">About Backups:</strong> Backups are stored as ZIP files containing your Fightarr database.
            Configure backup folder and retention settings in Media Management settings.
            After restoring a backup, you must restart Fightarr for changes to take effect.
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
                  ⚠️ Your current database will be backed up before restoration, but you will need to restart Fightarr after the restore.
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
    </div>
  );
};

export default BackupPage;
