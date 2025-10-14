import { useState } from 'react';
import { PlusIcon, PencilIcon, TrashIcon, DocumentArrowDownIcon, ClipboardDocumentIcon } from '@heroicons/react/24/outline';

interface CustomFormatsSettingsProps {
  showAdvanced: boolean;
}

interface CustomFormat {
  id: number;
  name: string;
  includeCustomFormatWhenRenaming: boolean;
  specifications: CustomFormatSpecification[];
}

interface CustomFormatSpecification {
  name: string;
  implementation: string;
  negate: boolean;
  required: boolean;
  fields: Record<string, any>;
}

export default function CustomFormatsSettings({ showAdvanced }: CustomFormatsSettingsProps) {
  const [customFormats, setCustomFormats] = useState<CustomFormat[]>([]);

  const [importJson, setImportJson] = useState('');
  const [showImportModal, setShowImportModal] = useState(false);
  const [editingFormat, setEditingFormat] = useState<CustomFormat | null>(null);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState<number | null>(null);

  const handleDeleteFormat = (id: number) => {
    setCustomFormats((prev) => prev.filter((f) => f.id !== id));
    setShowDeleteConfirm(null);
  };

  const handleExportFormat = (format: CustomFormat) => {
    const json = JSON.stringify(format, null, 2);
    const blob = new Blob([json], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${format.name.replace(/\s+/g, '_')}.json`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  };

  return (
    <div className="max-w-6xl">
      <div className="mb-8">
        <h2 className="text-3xl font-bold text-white mb-2">Custom Formats</h2>
        <p className="text-gray-400">
          Custom Formats allow fine control over release scoring and prioritization (TRaSH Guides compatible)
        </p>
      </div>

      {/* Info Box */}
      <div className="mb-8 bg-gradient-to-br from-purple-950/30 to-purple-900/20 border border-purple-900/50 rounded-lg p-6">
        <div className="flex items-start">
          <DocumentArrowDownIcon className="w-6 h-6 text-purple-400 mr-3 flex-shrink-0 mt-0.5" />
          <div>
            <h3 className="text-lg font-semibold text-white mb-2">Custom Format Import</h3>
            <p className="text-sm text-gray-300 mb-3">
              Import custom formats from JSON files for optimal quality settings for combat sports content.
            </p>
            <div className="flex items-center space-x-3">
              <button
                onClick={() => setShowImportModal(true)}
                className="px-4 py-2 bg-purple-600 hover:bg-purple-700 text-white text-sm font-medium rounded-lg transition-colors"
              >
                <DocumentArrowDownIcon className="w-4 h-4 inline mr-2" />
                Import from JSON
              </button>
              <a
                href="https://trash-guides.info/"
                target="_blank"
                rel="noopener noreferrer"
                className="text-sm text-purple-400 hover:text-purple-300 underline"
              >
                Visit TRaSH Guides →
              </a>
            </div>
          </div>
        </div>
      </div>

      {/* Custom Formats List */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <div className="flex items-center justify-between mb-6">
          <h3 className="text-xl font-semibold text-white">Your Custom Formats</h3>
          <button className="flex items-center px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors">
            <PlusIcon className="w-4 h-4 mr-2" />
            Add Custom Format
          </button>
        </div>

        <div className="space-y-3">
          {customFormats.map((format) => (
            <div
              key={format.id}
              className="group bg-black/30 border border-gray-800 hover:border-red-900/50 rounded-lg p-4 transition-all"
            >
              <div className="flex items-start justify-between">
                <div className="flex-1">
                  <h4 className="text-lg font-semibold text-white mb-2">{format.name}</h4>
                  <div className="space-y-1">
                    {format.specifications.map((spec, index) => (
                      <div key={index} className="flex items-center text-sm">
                        <span
                          className={`px-2 py-0.5 rounded text-xs mr-2 ${
                            spec.required
                              ? 'bg-red-900/30 text-red-400'
                              : 'bg-blue-900/30 text-blue-400'
                          }`}
                        >
                          {spec.required ? 'Required' : 'Optional'}
                        </span>
                        <span className="text-gray-400">{spec.implementation}:</span>
                        <code className="ml-2 px-2 py-0.5 bg-gray-900 text-green-400 rounded text-xs font-mono">
                          {spec.fields.value || 'N/A'}
                        </code>
                        {spec.negate && (
                          <span className="ml-2 px-2 py-0.5 bg-yellow-900/30 text-yellow-400 text-xs rounded">
                            Negated
                          </span>
                        )}
                      </div>
                    ))}
                  </div>
                  {format.includeCustomFormatWhenRenaming && (
                    <div className="mt-2">
                      <span className="px-2 py-0.5 bg-green-900/30 text-green-400 text-xs rounded">
                        Included in file naming
                      </span>
                    </div>
                  )}
                </div>
                <div className="flex items-center space-x-2 ml-4">
                  <button
                    onClick={() => handleExportFormat(format)}
                    className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                    title="Export to JSON"
                  >
                    <ClipboardDocumentIcon className="w-5 h-5" />
                  </button>
                  <button
                    onClick={() => setEditingFormat(format)}
                    className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                    title="Edit"
                  >
                    <PencilIcon className="w-5 h-5" />
                  </button>
                  <button
                    onClick={() => setShowDeleteConfirm(format.id)}
                    className="p-2 text-gray-400 hover:text-red-400 hover:bg-red-950/30 rounded transition-colors"
                    title="Delete"
                  >
                    <TrashIcon className="w-5 h-5" />
                  </button>
                </div>
              </div>
            </div>
          ))}
        </div>

        {customFormats.length === 0 && (
          <div className="text-center py-12">
            <p className="text-gray-500 mb-4">No custom formats configured</p>
            <p className="text-sm text-gray-400">
              Add custom formats to score and prioritize releases based on specific criteria
            </p>
          </div>
        )}
      </div>

      {/* Condition Types Info */}
      {showAdvanced && (
        <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-yellow-900/30 rounded-lg p-6">
          <h3 className="text-xl font-semibold text-white mb-4">
            Available Condition Types
            <span className="ml-2 px-2 py-0.5 bg-yellow-900/30 text-yellow-400 text-xs rounded">
              Advanced
            </span>
          </h3>
          <div className="grid grid-cols-2 gap-4 text-sm">
            <div>
              <h4 className="text-white font-medium mb-2">Release Conditions:</h4>
              <ul className="space-y-1 text-gray-400">
                <li>• ReleaseTitle - Match against release name</li>
                <li>• Size - Match based on file size</li>
                <li>• Language - Match audio language</li>
                <li>• Resolution - Match video resolution</li>
                <li>• Source - Match source type (WEB, Bluray, etc.)</li>
              </ul>
            </div>
            <div>
              <h4 className="text-white font-medium mb-2">Event Conditions:</h4>
              <ul className="space-y-1 text-gray-400">
                <li>• Organization - Match organization (UFC, Bellator, etc.)</li>
                <li>• EventDate - Match event date range</li>
                <li>• Indexer - Match specific indexer</li>
                <li>• IndexerFlag - Match indexer flags</li>
              </ul>
            </div>
          </div>
        </div>
      )}

      {/* How to Use */}
      <div className="mb-8 bg-blue-950/30 border border-blue-900/50 rounded-lg p-6">
        <h3 className="text-lg font-semibold text-white mb-3">How to Use Custom Formats</h3>
        <ol className="space-y-2 text-sm text-gray-300">
          <li className="flex items-start">
            <span className="text-red-400 mr-2 font-bold">1.</span>
            <span>
              Create or import custom formats with conditions that match your preferences
            </span>
          </li>
          <li className="flex items-start">
            <span className="text-red-400 mr-2 font-bold">2.</span>
            <span>
              Go to <strong>Settings → Profiles</strong> and assign scores to custom formats in each quality
              profile
            </span>
          </li>
          <li className="flex items-start">
            <span className="text-red-400 mr-2 font-bold">3.</span>
            <span>
              Fightarr will score releases based on matching custom formats and prefer higher-scoring releases
            </span>
          </li>
          <li className="flex items-start">
            <span className="text-red-400 mr-2 font-bold">4.</span>
            <span>
              Set minimum and cutoff format scores in profiles to control upgrade behavior
            </span>
          </li>
        </ol>
      </div>

      {/* Save Button */}
      <div className="flex justify-end">
        <button className="px-6 py-3 bg-gradient-to-r from-red-600 to-red-700 hover:from-red-700 hover:to-red-800 text-white font-semibold rounded-lg shadow-lg transform transition hover:scale-105">
          Save Changes
        </button>
      </div>

      {/* Import Modal */}
      {showImportModal && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-purple-900/50 rounded-lg p-6 max-w-2xl w-full">
            <h3 className="text-2xl font-bold text-white mb-4">Import Custom Format from JSON</h3>
            <p className="text-gray-400 mb-4">
              Paste the JSON from TRaSH Guides or another Fightarr instance
            </p>
            <textarea
              value={importJson}
              onChange={(e) => setImportJson(e.target.value)}
              placeholder='{"name":"Format Name","specifications":[...]}'
              className="w-full h-64 px-4 py-3 bg-gray-800 border border-gray-700 rounded-lg text-white font-mono text-sm focus:outline-none focus:border-purple-600 resize-none"
            />
            <div className="mt-6 flex items-center justify-end space-x-3">
              <button
                onClick={() => {
                  setShowImportModal(false);
                  setImportJson('');
                }}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button className="px-4 py-2 bg-purple-600 hover:bg-purple-700 text-white rounded-lg transition-colors">
                Import Custom Format
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Delete Confirmation Modal */}
      {showDeleteConfirm !== null && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-md w-full">
            <h3 className="text-2xl font-bold text-white mb-4">Delete Custom Format?</h3>
            <p className="text-gray-400 mb-6">
              Are you sure you want to delete this custom format? This action cannot be undone.
            </p>
            <div className="flex items-center justify-end space-x-3">
              <button
                onClick={() => setShowDeleteConfirm(null)}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={() => handleDeleteFormat(showDeleteConfirm)}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
              >
                Delete
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Edit Modal (placeholder for now) */}
      {editingFormat && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-purple-900/50 rounded-lg p-6 max-w-3xl w-full max-h-[80vh] overflow-y-auto">
            <h3 className="text-2xl font-bold text-white mb-4">Edit Custom Format</h3>
            <p className="text-gray-400 mb-4">
              Editing: <strong className="text-white">{editingFormat.name}</strong>
            </p>
            <p className="text-sm text-gray-500 mb-6">
              Full edit functionality will be implemented in a future update. For now, you can export, delete, and re-import formats.
            </p>
            <div className="flex items-center justify-end space-x-3">
              <button
                onClick={() => setEditingFormat(null)}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
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
