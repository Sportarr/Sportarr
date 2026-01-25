import { useState, useEffect, useRef } from 'react';
import { InformationCircleIcon, ArrowDownTrayIcon, CheckCircleIcon, XCircleIcon } from '@heroicons/react/24/outline';
import { toast } from 'sonner';
import SettingsHeader from '../../components/SettingsHeader';
import { useUnsavedChanges } from '../../hooks/useUnsavedChanges';
import { apiGet, apiPut, apiPost } from '../../utils/api';

interface QualitySettingsProps {
  showAdvanced?: boolean;
}

interface QualityDefinition {
  id: number;
  quality: number;
  title: string;
  minSize: number;
  maxSize: number | null;
  preferredSize: number;
}

interface TrashImportResult {
  success: boolean;
  error?: string;
  created: number;
  updated: number;
  syncedFormats: string[];
}

export default function QualitySettings({ showAdvanced = false }: QualitySettingsProps) {
  const [qualityDefinitions, setQualityDefinitions] = useState<QualityDefinition[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [hasUnsavedChanges, setHasUnsavedChanges] = useState(false);
  const initialDefinitions = useRef<QualityDefinition[] | null>(null);
  const { blockNavigation } = useUnsavedChanges(hasUnsavedChanges);

  // TRaSH import state
  const [importing, setImporting] = useState(false);
  const [importResult, setImportResult] = useState<TrashImportResult | null>(null);
  const [showResultModal, setShowResultModal] = useState(false);

  useEffect(() => {
    loadQualityDefinitions();
  }, []);

  const loadQualityDefinitions = async () => {
    try {
      const response = await apiGet('/api/qualitydefinition');
      if (response.ok) {
        const data = await response.json();
        setQualityDefinitions(data);
        initialDefinitions.current = data;
        setHasUnsavedChanges(false);
      }
    } catch (error) {
      console.error('Failed to load quality definitions:', error);
    } finally {
      setLoading(false);
    }
  };

  // Detect changes
  useEffect(() => {
    if (!initialDefinitions.current) return;
    const hasChanges = JSON.stringify(qualityDefinitions) !== JSON.stringify(initialDefinitions.current);
    setHasUnsavedChanges(hasChanges);
  }, [qualityDefinitions]);

  const handleQualityChange = (id: number, field: 'minSize' | 'maxSize' | 'preferredSize', value: number) => {
    setQualityDefinitions((prev) =>
      prev.map((q) => (q.id === id ? { ...q, [field]: value } : q))
    );
  };

  const handleSave = async () => {
    setSaving(true);
    try {
      const response = await apiPut('/api/qualitydefinition/bulk', qualityDefinitions);

      if (response.ok) {
        // Show sync paused notification
        toast.warning('TRaSH Auto-Sync Paused', {
          description: 'Quality size auto-sync has been disabled. Import from TRaSH Guides to re-enable.',
          duration: 6000,
        });

        // Reload from backend to get the saved data
        // loadQualityDefinitions already sets both qualityDefinitions and initialDefinitions.current
        await loadQualityDefinitions();
        // Note: Don't set initialDefinitions.current here - loadQualityDefinitions handles it
        // Setting it here with the stale qualityDefinitions value causes the unsaved indicator to flash
      }
    } catch (error) {
      console.error('Failed to save quality definitions:', error);
    } finally {
      setSaving(false);
    }
  };

  const handleImportFromTrash = async () => {
    setImporting(true);
    setImportResult(null);

    try {
      const response = await apiPost('/api/qualitydefinition/trash/import', {});

      const result = await response.json();
      setImportResult(result);
      setShowResultModal(true);

      if (result.success) {
        // Reload quality definitions to show updated values
        await loadQualityDefinitions();
      }
    } catch (error) {
      console.error('Failed to import from TRaSH:', error);
      setImportResult({
        success: false,
        error: 'Failed to connect to server',
        created: 0,
        updated: 0,
        syncedFormats: [],
      });
      setShowResultModal(true);
    } finally {
      setImporting(false);
    }
  };

  if (loading) {
    return (
      <div className="max-w-6xl mx-auto text-center py-12">
        <div className="text-gray-400">Loading quality definitions...</div>
      </div>
    );
  }

  return (
    <div>
      <SettingsHeader
        title="Quality Definitions"
        subtitle="Quality settings control file size limits for each quality level"
        onSave={handleSave}
        isSaving={saving}
        hasUnsavedChanges={hasUnsavedChanges}
        saveButtonText="Save Changes"
      />

      <div className="max-w-6xl mx-auto px-6">

      {/* Info Box */}
      <div className="mb-8 bg-blue-950/30 border border-blue-900/50 rounded-lg p-6">
        <div className="flex items-start">
          <InformationCircleIcon className="w-6 h-6 text-blue-400 mr-3 flex-shrink-0 mt-0.5" />
          <div>
            <h3 className="text-lg font-semibold text-white mb-2">About Quality Definitions</h3>
            <ul className="space-y-2 text-sm text-gray-300">
              <li className="flex items-start">
                <span className="text-red-400 mr-2">•</span>
                <span>
                  <strong>Min Size:</strong> Minimum file size in MB per minute. Files smaller will be rejected.
                  Example: 15 MB/min × 180 min = 2.7 GB minimum for a 3-hour event.
                </span>
              </li>
              <li className="flex items-start">
                <span className="text-red-400 mr-2">•</span>
                <span>
                  <strong>Max Size:</strong> Maximum file size in MB per minute. Files larger will be rejected.
                  Set high (e.g., 1000) for effectively unlimited.
                </span>
              </li>
              <li className="flex items-start">
                <span className="text-red-400 mr-2">•</span>
                <span>
                  <strong>Preferred Size:</strong> Target size for tiebreaking. When releases have equal scores,
                  Sportarr prefers files closer to this size (rounded to 200MB chunks like Sonarr).
                </span>
              </li>
              <li className="flex items-start">
                <span className="text-red-400 mr-2">•</span>
                <span>
                  Sports events typically run 2-4 hours (120-240 min). Values match Sonarr/Radarr quality definitions
                  from TRaSH Guides.
                </span>
              </li>
            </ul>
          </div>
        </div>
      </div>

      {/* Import from TRaSH Guides */}
      <div className="mb-6 flex items-center justify-between">
        <p className="text-sm text-gray-400">
          Sizes are in MB per minute of runtime. Adjust based on your preferences and storage capacity.
        </p>
        <button
          onClick={handleImportFromTrash}
          disabled={importing}
          className="px-4 py-2 bg-gradient-to-r from-purple-600 to-purple-700 hover:from-purple-700 hover:to-purple-800 disabled:from-purple-800 disabled:to-purple-900 disabled:opacity-50 text-white font-medium rounded-lg transition-all inline-flex items-center"
        >
          {importing ? (
            <>
              <svg className="animate-spin -ml-1 mr-2 h-4 w-4 text-white" fill="none" viewBox="0 0 24 24">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
              </svg>
              Importing...
            </>
          ) : (
            <>
              <ArrowDownTrayIcon className="w-4 h-4 mr-2" />
              Import from TRaSH Guides
            </>
          )}
        </button>
      </div>

      {/* Quality Definitions Table */}
      <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead>
              <tr className="bg-black/50 border-b border-red-900/30">
                <th className="px-6 py-4 text-left text-sm font-semibold text-white">Quality</th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-white">
                  Min Size
                  <span className="block text-xs text-gray-400 font-normal">(MB/min)</span>
                </th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-white">
                  Preferred
                  <span className="block text-xs text-gray-400 font-normal">(MB/min)</span>
                </th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-white">
                  Max Size
                  <span className="block text-xs text-gray-400 font-normal">(MB/min)</span>
                </th>
                <th className="px-6 py-4 text-left text-sm font-semibold text-white">
                  Range
                  <span className="block text-xs text-gray-400 font-normal">(3hr/180min event)</span>
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-800">
              {qualityDefinitions.length === 0 ? (
                <tr>
                  <td colSpan={5} className="px-6 py-12 text-center">
                    <p className="text-gray-500 mb-2">No quality definitions found</p>
                    <p className="text-sm text-gray-400">
                      Quality definitions should be seeded automatically. Try restarting the application.
                    </p>
                  </td>
                </tr>
              ) : (
                qualityDefinitions.map((quality, index) => (
                  <tr
                    key={quality.id}
                    className={index % 2 === 0 ? 'bg-black/20' : 'bg-black/10'}
                  >
                  <td className="px-6 py-4">
                    <span className="text-white font-medium">{quality.title}</span>
                  </td>
                  <td className="px-6 py-4">
                    <input
                      type="number"
                      value={quality.minSize}
                      onChange={(e) =>
                        handleQualityChange(quality.id, 'minSize', Number(e.target.value))
                      }
                      className="w-24 px-3 py-2 bg-gray-800 border border-gray-700 rounded text-white text-sm focus:outline-none focus:border-red-600"
                      step="0.5"
                      min="0"
                    />
                  </td>
                  <td className="px-6 py-4">
                    <input
                      type="number"
                      value={quality.preferredSize}
                      onChange={(e) =>
                        handleQualityChange(quality.id, 'preferredSize', Number(e.target.value))
                      }
                      className="w-24 px-3 py-2 bg-gray-800 border border-gray-700 rounded text-white text-sm focus:outline-none focus:border-red-600"
                      step="0.5"
                      min="0"
                    />
                  </td>
                  <td className="px-6 py-4">
                    <input
                      type="number"
                      value={quality.maxSize ?? ''}
                      onChange={(e) =>
                        handleQualityChange(quality.id, 'maxSize', Number(e.target.value))
                      }
                      className="w-24 px-3 py-2 bg-gray-800 border border-gray-700 rounded text-white text-sm focus:outline-none focus:border-red-600"
                      step="0.5"
                      min="0"
                    />
                  </td>
                  <td className="px-6 py-4">
                    <span className="text-gray-400 text-sm">
                      {((quality.minSize * 180) / 1024).toFixed(1)} -{' '}
                      {quality.maxSize ? ((quality.maxSize * 180) / 1024).toFixed(1) : '∞'} GB
                    </span>
                  </td>
                </tr>
              ))
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* Warning for Advanced Users */}
      {showAdvanced && (
        <div className="mt-6 p-4 bg-yellow-950/30 border border-yellow-900/50 rounded-lg">
          <p className="text-sm text-yellow-300">
            <strong>Advanced:</strong> These values affect all quality profiles. Changes here impact which
            releases are accepted across your entire library. Be careful when adjusting these settings.
          </p>
        </div>
      )}
      </div>

      {/* Result Modal */}
      {showResultModal && importResult && (
        <div className="fixed inset-0 bg-black/70 flex items-center justify-center z-50">
          <div className="bg-gray-900 border border-gray-700 rounded-lg w-full max-w-md mx-4 shadow-xl">
            <div className="p-6">
              {importResult.success ? (
                <div className="flex items-start">
                  <CheckCircleIcon className="w-8 h-8 text-green-400 mr-4 flex-shrink-0" />
                  <div>
                    <h3 className="text-lg font-semibold text-white mb-2">Import Successful</h3>
                    <p className="text-gray-300">
                      Updated {importResult.updated} quality definitions
                      {importResult.created > 0 && `, created ${importResult.created} new`}
                    </p>
                    <p className="text-sm text-gray-400 mt-2">
                      Quality sizes have been set to TRaSH Guides recommended values.
                    </p>
                  </div>
                </div>
              ) : (
                <div className="flex items-start">
                  <XCircleIcon className="w-8 h-8 text-red-400 mr-4 flex-shrink-0" />
                  <div>
                    <h3 className="text-lg font-semibold text-white mb-2">Import Failed</h3>
                    <p className="text-gray-300">{importResult.error}</p>
                  </div>
                </div>
              )}
            </div>
            <div className="p-4 border-t border-gray-700 flex justify-end">
              <button
                onClick={() => setShowResultModal(false)}
                className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded-lg transition-colors"
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
