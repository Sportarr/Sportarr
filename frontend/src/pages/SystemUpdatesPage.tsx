import React, { useState, useEffect } from 'react';
import { ArrowDownTrayIcon, CheckCircleIcon, ExclamationCircleIcon, ArrowPathIcon } from '@heroicons/react/24/outline';
import PageHeader from '../components/PageHeader';
import PageShell from '../components/PageShell';
import { apiGet } from '../utils/api';
import { parseAsUtc } from '../utils/timezone';
import { useCompactView } from '../hooks/useCompactView';
import { TABLE_ROW_HOVER, TABLE_CELL_LABEL, TABLE_CELL_DATA } from '../utils/designTokens';

interface Release {
  version: string;
  releaseDate: string;
  branch: string;
  changes: string[];
  downloadUrl?: string;
  isInstalled: boolean;
  isLatest: boolean;
}

interface UpdateInfo {
  updateAvailable: boolean;
  currentVersion: string;
  latestVersion: string;
  releases: Release[];
}

const SystemUpdatesPage: React.FC = () => {
  const [updateInfo, setUpdateInfo] = useState<UpdateInfo | null>(null);
  const [loading, setLoading] = useState(true);
  const [checking, setChecking] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const compactView = useCompactView();

  useEffect(() => {
    checkForUpdates();
  }, []);

  const checkForUpdates = async () => {
    setChecking(true);
    setError(null);

    try {
      const response = await apiGet('/api/system/updates');

      if (!response.ok) {
        throw new Error('Failed to check for updates');
      }

      const data: UpdateInfo = await response.json();
      setUpdateInfo(data);
    } catch (err) {
      console.error('Error checking for updates:', err);
      setError(err instanceof Error ? err.message : 'An error occurred');
    } finally {
      setLoading(false);
      setChecking(false);
    }
  };

  const formatDate = (dateString: string) => {
    return parseAsUtc(dateString).toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'long',
      day: 'numeric'
    });
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-gray-400">Checking for updates...</div>
      </div>
    );
  }

  return (
    <PageShell>
      <PageHeader
        title="Updates"
        subtitle="Check for new versions and view release history"
        actions={
          <button
            onClick={checkForUpdates}
            disabled={checking}
            className="flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-white transition-colors hover:bg-blue-700 disabled:opacity-50"
          >
            <ArrowPathIcon className={`w-5 h-5 ${checking ? 'animate-spin' : ''}`} />
            {checking ? 'Checking...' : 'Check for Updates'}
          </button>
        }
      />

      {/* Error Message */}
      {error && (
        <div className="mb-6 bg-red-900/20 border border-red-800 rounded-lg p-4">
          <div className="flex items-start gap-3">
            <ExclamationCircleIcon className="w-6 h-6 text-red-400 flex-shrink-0 mt-0.5" />
            <div>
              <h3 className="text-lg font-semibold text-red-400 mb-1">Error Checking for Updates</h3>
              <p className="text-red-400/80">{error}</p>
            </div>
          </div>
        </div>
      )}

      {/* Current Status */}
      {updateInfo && (
        <>
          <div className={`mb-6 rounded-lg p-6 border ${
            updateInfo.updateAvailable
              ? 'bg-blue-900/20 border-blue-800'
              : 'bg-green-900/20 border-green-800'
          }`}>
            <div className="flex items-start gap-4">
              {updateInfo.updateAvailable ? (
                <ExclamationCircleIcon className="w-8 h-8 text-blue-400 flex-shrink-0" />
              ) : (
                <CheckCircleIcon className="w-8 h-8 text-green-400 flex-shrink-0" />
              )}
              <div className="flex-1">
                <h2 className={`text-2xl font-bold mb-2 ${
                  updateInfo.updateAvailable ? 'text-blue-400' : 'text-green-400'
                }`}>
                  {updateInfo.updateAvailable ? 'Update Available!' : 'Up to Date'}
                </h2>
                <div className="space-y-2 text-gray-300">
                  <p>
                    <span className="text-gray-400">Current Version:</span>{' '}
                    <span className="font-semibold text-white">{updateInfo.currentVersion}</span>
                  </p>
                  {updateInfo.updateAvailable && (
                    <p>
                      <span className="text-gray-400">Latest Version:</span>{' '}
                      <span className="font-semibold text-blue-400">{updateInfo.latestVersion}</span>
                    </p>
                  )}
                </div>
                {updateInfo.updateAvailable && (
                  <div className="mt-4 p-4 bg-blue-900/30 border border-blue-700 rounded-lg">
                    <p className="text-sm text-blue-300">
                      <strong>Docker Users:</strong> To update, pull the latest image and restart your container:
                    </p>
                    <pre className="mt-2 p-3 bg-gray-900 border border-gray-700 rounded text-xs text-gray-300 overflow-x-auto">
docker pull ghcr.io/sportarr/sportarr:latest{'\n'}
docker restart sportarr
                    </pre>
                  </div>
                )}
              </div>
            </div>
          </div>

          {/* Releases List */}
          {compactView ? (
            <div className="bg-gray-800 rounded-lg border border-gray-700 overflow-x-auto mb-6">
              <div className="p-4 border-b border-gray-700">
                <h2 className="text-xl font-semibold text-white">Release History</h2>
              </div>
              <table className="w-full">
                <thead>
                  <tr className="text-xs text-gray-400 uppercase text-left border-b border-gray-700">
                    <th className="px-3 py-1.5">Version</th>
                    <th className="px-2 py-1.5">Branch</th>
                    <th className="px-2 py-1.5">Date</th>
                    <th className="px-3 py-1.5">Changes</th>
                    <th className="px-2 py-1.5">Status</th>
                    <th className="px-2 py-1.5"></th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-700">
                  {updateInfo.releases.map((release) => (
                    <tr
                      key={release.version}
                      className={`${TABLE_ROW_HOVER} ${release.isInstalled ? 'bg-gray-900/50' : ''}`}
                    >
                      <td className={`${TABLE_CELL_LABEL} font-bold text-white`}>
                        {release.version}
                      </td>
                      <td className={`${TABLE_CELL_DATA} text-gray-400`}>
                        {release.branch}
                      </td>
                      <td className={`${TABLE_CELL_DATA} text-gray-400 whitespace-nowrap`}>
                        {formatDate(release.releaseDate)}
                      </td>
                      <td className={`${TABLE_CELL_LABEL} text-gray-300`}>
                        {release.changes && release.changes.length > 0 ? (
                          <ul className="space-y-0.5">
                            {release.changes.map((change, i) => (
                              <li key={i} className="flex items-start gap-1 text-xs">
                                <span className="text-gray-600 mt-0.5">•</span>
                                <span>{change}</span>
                              </li>
                            ))}
                          </ul>
                        ) : (
                          <span className="text-gray-600 text-xs">—</span>
                        )}
                      </td>
                      <td className={`${TABLE_CELL_DATA}`}>
                        {release.isInstalled && (
                          <span className="px-2 py-0.5 bg-green-900/50 border border-green-700 text-green-400 text-xs rounded">
                            Installed
                          </span>
                        )}
                        {release.isLatest && !release.isInstalled && (
                          <span className="px-2 py-0.5 bg-blue-900/50 border border-blue-700 text-blue-400 text-xs rounded">
                            Latest
                          </span>
                        )}
                      </td>
                      <td className={`${TABLE_CELL_DATA}`}>
                        {release.downloadUrl && (
                          <a
                            href={release.downloadUrl}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="inline-flex items-center gap-1 px-2 py-1 bg-gray-700 hover:bg-gray-600 text-white text-xs rounded transition-colors"
                          >
                            <ArrowDownTrayIcon className="w-3.5 h-3.5" />
                            GitHub
                          </a>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : (
            <div className="mb-6">
              <h2 className="text-xl font-semibold text-white mb-3">Release History</h2>
              <div className="flex flex-col gap-3">
                {updateInfo.releases.map((release) => (
                  <div
                    key={release.version}
                    className={`bg-gray-800 border border-gray-700 rounded-lg p-4 ${
                      release.isInstalled ? 'bg-gray-900/50' : ''
                    }`}
                  >
                    <div className="flex items-start justify-between mb-3">
                      <div className="flex items-center gap-3 flex-wrap">
                        <h3 className="text-lg font-bold text-white">
                          Version {release.version}
                        </h3>
                        {release.isInstalled && (
                          <span className="px-2 py-1 bg-green-900/50 border border-green-700 text-green-400 text-xs font-medium rounded">
                            Installed
                          </span>
                        )}
                        {release.isLatest && !release.isInstalled && (
                          <span className="px-2 py-1 bg-blue-900/50 border border-blue-700 text-blue-400 text-xs font-medium rounded">
                            Latest
                          </span>
                        )}
                        <span className="text-sm text-gray-500">{release.branch}</span>
                      </div>
                      <div className="text-sm text-gray-400 whitespace-nowrap ml-4">
                        {formatDate(release.releaseDate)}
                      </div>
                    </div>

                    {release.changes && release.changes.length > 0 && (
                      <div className="mb-3">
                        <h4 className="text-sm font-semibold text-gray-400 mb-2">Changes:</h4>
                        <ul className="space-y-1">
                          {release.changes.map((change, index) => (
                            <li key={index} className="text-sm text-gray-300 flex items-start gap-2">
                              <span className="text-gray-600 mt-1">•</span>
                              <span className="flex-1">{change}</span>
                            </li>
                          ))}
                        </ul>
                      </div>
                    )}

                    {release.downloadUrl && (
                      <a
                        href={release.downloadUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="inline-flex items-center gap-2 px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white text-sm rounded-lg transition-colors"
                      >
                        <ArrowDownTrayIcon className="w-4 h-4" />
                        View on GitHub
                      </a>
                    )}
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* Info Box */}
          <div className="p-4 bg-gray-800 border border-gray-700 rounded-lg">
            <div className="flex items-start gap-3">
              <ExclamationCircleIcon className="w-5 h-5 text-blue-400 flex-shrink-0 mt-0.5" />
              <div className="text-sm text-gray-300">
                <strong className="text-white">Update Instructions:</strong>
                <ul className="mt-2 space-y-1 list-disc list-inside">
                  <li>Updates are pulled from the GitHub repository releases</li>
                  <li>Docker users should pull the latest image and restart the container</li>
                  <li>Manual installations should download the latest release from GitHub</li>
                  <li>Always backup your database before updating</li>
                </ul>
              </div>
            </div>
          </div>
        </>
      )}
    </PageShell>
  );
};

export default SystemUpdatesPage;
