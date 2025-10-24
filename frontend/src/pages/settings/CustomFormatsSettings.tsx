import { useState, useEffect } from 'react';
import { PlusIcon, PencilIcon, TrashIcon, DocumentArrowDownIcon, ClipboardDocumentIcon, XMarkIcon } from '@heroicons/react/24/outline';

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
  id?: number;
  name: string;
  implementation: string;
  negate: boolean;
  required: boolean;
  fields: Record<string, any>;
}

const CONDITION_TYPES = [
  { value: 'ReleaseTitle', label: 'Release Title', hasPresets: true },
  { value: 'Language', label: 'Language', hasPresets: true },
  { value: 'IndexerFlag', label: 'Indexer Flag', hasPresets: false },
  { value: 'Source', label: 'Source', hasPresets: true },
  { value: 'Resolution', label: 'Resolution', hasPresets: true },
  { value: 'Size', label: 'Size', hasPresets: false },
  { value: 'ReleaseGroup', label: 'Release Group', hasPresets: true },
  { value: 'ReleaseType', label: 'Release Type', hasPresets: false },
];

const SOURCE_PRESETS = ['BluRay', 'WEB-DL', 'WEBDL', 'WEBRip', 'HDTV', 'DVDRip', 'CAM', 'TELESYNC'];
const RESOLUTION_PRESETS = ['2160p', '1080p', '720p', '480p', '4K', 'UHD', 'HD', 'SD'];
const LANGUAGE_PRESETS = ['English', 'Spanish', 'French', 'Japanese', 'Portuguese'];

export default function CustomFormatsSettings({ showAdvanced }: CustomFormatsSettingsProps) {
  const [customFormats, setCustomFormats] = useState<CustomFormat[]>([]);
  const [loading, setLoading] = useState(true);

  const [importJson, setImportJson] = useState('');
  const [showImportModal, setShowImportModal] = useState(false);
  const [showAddModal, setShowAddModal] = useState(false);
  const [editingFormat, setEditingFormat] = useState<CustomFormat | null>(null);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState<number | null>(null);
  const [showConditionModal, setShowConditionModal] = useState(false);

  // Form state for format creation/editing
  const [formData, setFormData] = useState<CustomFormat>({
    id: 0,
    name: '',
    includeCustomFormatWhenRenaming: false,
    specifications: []
  });

  // Condition builder state
  const [conditionForm, setConditionForm] = useState<CustomFormatSpecification>({
    name: '',
    implementation: 'ReleaseTitle',
    negate: false,
    required: false,
    fields: { value: '' }
  });

  // Load custom formats from API
  useEffect(() => {
    loadCustomFormats();
  }, []);

  const loadCustomFormats = async () => {
    try {
      const response = await fetch('/api/customformat');
      if (response.ok) {
        const data = await response.json();
        setCustomFormats(data);
      }
    } catch (error) {
      console.error('Error loading custom formats:', error);
    } finally {
      setLoading(false);
    }
  };

  const handleSaveFormat = async () => {
    if (!formData.name.trim()) {
      alert('Please enter a format name');
      return;
    }

    try {
      const url = editingFormat ? `/api/customformat/${editingFormat.id}` : '/api/customformat';
      const method = editingFormat ? 'PUT' : 'POST';

      const response = await fetch(url, {
        method,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(formData),
      });

      if (response.ok) {
        await loadCustomFormats();
        setShowAddModal(false);
        setEditingFormat(null);
        setFormData({
          id: 0,
          name: '',
          includeCustomFormatWhenRenaming: false,
          specifications: []
        });
      } else {
        alert('Error saving custom format');
      }
    } catch (error) {
      console.error('Error saving custom format:', error);
      alert('Error saving custom format');
    }
  };

  const handleDeleteFormat = async (id: number) => {
    try {
      const response = await fetch(`/api/customformat/${id}`, {
        method: 'DELETE',
      });

      if (response.ok) {
        await loadCustomFormats();
        setShowDeleteConfirm(null);
      } else {
        alert('Error deleting custom format');
      }
    } catch (error) {
      console.error('Error deleting custom format:', error);
      alert('Error deleting custom format');
    }
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

  const handleImportFormat = async () => {
    try {
      const parsed = JSON.parse(importJson);

      if (!parsed.name || !Array.isArray(parsed.specifications)) {
        alert('Invalid custom format JSON structure');
        return;
      }

      // Create via API
      const response = await fetch('/api/customformat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: parsed.name,
          includeCustomFormatWhenRenaming: parsed.includeCustomFormatWhenRenaming || false,
          specifications: parsed.specifications || []
        }),
      });

      if (response.ok) {
        await loadCustomFormats();
        setShowImportModal(false);
        setImportJson('');
      } else {
        alert('Error importing custom format');
      }
    } catch (error) {
      console.error('Error parsing JSON:', error);
      alert('Invalid JSON format');
    }
  };

  const handleAddCondition = () => {
    if (!conditionForm.name.trim()) {
      alert('Please enter a condition name');
      return;
    }

    if (conditionForm.implementation !== 'Size' && !conditionForm.fields.value) {
      alert('Please enter a value for this condition');
      return;
    }

    setFormData(prev => ({
      ...prev,
      specifications: [...prev.specifications, { ...conditionForm }]
    }));

    // Reset condition form
    setConditionForm({
      name: '',
      implementation: 'ReleaseTitle',
      negate: false,
      required: false,
      fields: { value: '' }
    });
    setShowConditionModal(false);
  };

  const handleRemoveCondition = (index: number) => {
    setFormData(prev => ({
      ...prev,
      specifications: prev.specifications.filter((_, i) => i !== index)
    }));
  };

  const openEditModal = (format: CustomFormat) => {
    setEditingFormat(format);
    setFormData({ ...format });
    setShowAddModal(true);
  };

  const openAddModal = () => {
    setEditingFormat(null);
    setFormData({
      id: 0,
      name: '',
      includeCustomFormatWhenRenaming: false,
      specifications: []
    });
    setShowAddModal(true);
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-white">Loading custom formats...</div>
      </div>
    );
  }

  return (
    <div className="max-w-6xl mx-auto">
      <div className="mb-8">
        <h2 className="text-3xl font-bold text-white mb-2">Custom Formats</h2>
        <p className="text-gray-400">
          Custom Formats allow fine control over release scoring and prioritization
        </p>
      </div>

      {/* Info Box */}
      <div className="mb-8 bg-gradient-to-br from-purple-950/30 to-purple-900/20 border border-purple-900/50 rounded-lg p-6">
        <div className="flex items-start">
          <DocumentArrowDownIcon className="w-6 h-6 text-purple-400 mr-3 flex-shrink-0 mt-0.5" />
          <div>
            <h3 className="text-lg font-semibold text-white mb-2">Sonarr supports custom conditions against the release properties below.</h3>
            <p className="text-sm text-gray-300 mb-3">
              Visit the wiki for more details: <a href="https://wiki.servarr.com/sonarr/settings#custom-formats" target="_blank" rel="noopener noreferrer" className="text-purple-400 hover:text-purple-300 underline">Wiki</a>
            </p>
            <div className="flex items-center space-x-3">
              <button
                onClick={() => setShowImportModal(true)}
                className="px-4 py-2 bg-purple-600 hover:bg-purple-700 text-white text-sm font-medium rounded-lg transition-colors"
              >
                <DocumentArrowDownIcon className="w-4 h-4 inline mr-2" />
                Import
              </button>
            </div>
          </div>
        </div>
      </div>

      {/* Custom Formats List */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <div className="flex items-center justify-between mb-6">
          <h3 className="text-xl font-semibold text-white">Custom Formats</h3>
          <button
            onClick={openAddModal}
            className="flex items-center px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
          >
            <PlusIcon className="w-4 h-4 mr-2" />
            Add Custom Format
          </button>
        </div>

        {customFormats.length === 0 ? (
          <div className="text-center py-12">
            <p className="text-gray-500 mb-4">No custom formats configured</p>
            <p className="text-sm text-gray-400">
              Add custom formats to score and prioritize releases based on specific criteria
            </p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead>
                <tr className="border-b border-gray-800">
                  <th className="text-left py-3 px-4 text-gray-400 font-medium">Name</th>
                  <th className="text-left py-3 px-4 text-gray-400 font-medium">Conditions</th>
                  <th className="text-right py-3 px-4 text-gray-400 font-medium">Actions</th>
                </tr>
              </thead>
              <tbody>
                {customFormats.map((format) => (
                  <tr key={format.id} className="border-b border-gray-800 hover:bg-gray-900/50 transition-colors">
                    <td className="py-3 px-4">
                      <div className="flex flex-col">
                        <span className="text-white font-medium">{format.name}</span>
                        {format.includeCustomFormatWhenRenaming && (
                          <span className="text-xs text-green-400 mt-1">Included in renaming</span>
                        )}
                      </div>
                    </td>
                    <td className="py-3 px-4">
                      <span className="text-gray-400">{format.specifications.length} condition(s)</span>
                    </td>
                    <td className="py-3 px-4">
                      <div className="flex items-center justify-end space-x-2">
                        <button
                          onClick={() => handleExportFormat(format)}
                          className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                          title="Export to JSON"
                        >
                          <ClipboardDocumentIcon className="w-5 h-5" />
                        </button>
                        <button
                          onClick={() => openEditModal(format)}
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
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Import Modal */}
      {showImportModal && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-purple-900/50 rounded-lg p-6 max-w-2xl w-full">
            <div className="flex items-center justify-between mb-4">
              <h3 className="text-2xl font-bold text-white">Import Custom Format from JSON</h3>
              <button
                onClick={() => {
                  setShowImportModal(false);
                  setImportJson('');
                }}
                className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
              >
                <XMarkIcon className="w-6 h-6" />
              </button>
            </div>
            <p className="text-gray-400 mb-4">
              Paste the JSON from TRaSH Guides or export from another instance
            </p>
            <textarea
              value={importJson}
              onChange={(e) => setImportJson(e.target.value)}
              placeholder='{"name":"Format Name","includeCustomFormatWhenRenaming":false,"specifications":[...]}'
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
              <button
                onClick={handleImportFormat}
                className="px-4 py-2 bg-purple-600 hover:bg-purple-700 text-white rounded-lg transition-colors"
              >
                Import
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

      {/* Add/Edit Custom Format Modal */}
      {showAddModal && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4 overflow-y-auto">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-4xl w-full my-8">
            <div className="flex items-center justify-between mb-6">
              <h3 className="text-2xl font-bold text-white">
                {editingFormat ? 'Edit Custom Format' : 'Add Custom Format'}
              </h3>
              <button
                onClick={() => {
                  setShowAddModal(false);
                  setEditingFormat(null);
                }}
                className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
              >
                <XMarkIcon className="w-6 h-6" />
              </button>
            </div>

            <div className="space-y-6">
              {/* Name */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Name</label>
                <input
                  type="text"
                  value={formData.name}
                  onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                  placeholder="e.g., Preferred Source, Better Audio, etc."
                />
              </div>

              {/* Include in renaming checkbox */}
              <label className="flex items-center space-x-3 cursor-pointer">
                <input
                  type="checkbox"
                  checked={formData.includeCustomFormatWhenRenaming}
                  onChange={(e) => setFormData({ ...formData, includeCustomFormatWhenRenaming: e.target.checked })}
                  className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                />
                <span className="text-sm font-medium text-gray-300">
                  Include in {'{'} Custom Formats{'}'} renaming format
                </span>
              </label>

              {/* Conditions Section */}
              <div className="border-t border-gray-800 pt-6">
                <div className="flex items-center justify-between mb-4">
                  <h4 className="text-lg font-semibold text-white">Conditions</h4>
                  <button
                    onClick={() => setShowConditionModal(true)}
                    className="flex items-center px-3 py-1.5 bg-gray-800 hover:bg-gray-700 text-white rounded transition-colors"
                  >
                    <PlusIcon className="w-4 h-4 mr-1" />
                    Add Condition
                  </button>
                </div>

                <div className="p-4 bg-blue-950/30 border border-blue-900/50 rounded-lg mb-4">
                  <p className="text-sm text-blue-300">
                    A Custom Format will be applied to a release or file when it matches at least one of each of the different condition types chosen.
                  </p>
                </div>

                {formData.specifications.length === 0 ? (
                  <div className="p-8 bg-gray-800/50 rounded-lg text-center">
                    <p className="text-gray-500">No conditions added yet</p>
                    <p className="text-sm text-gray-400 mt-2">Click "Add Condition" to get started</p>
                  </div>
                ) : (
                  <div className="space-y-2">
                    {formData.specifications.map((spec, index) => (
                      <div key={index} className="p-3 bg-gray-800 rounded border border-gray-700 flex items-center justify-between">
                        <div className="flex-1">
                          <div className="flex items-center space-x-2 mb-1">
                            <span className="text-white font-medium">{spec.name}</span>
                            {spec.required && (
                              <span className="px-2 py-0.5 bg-red-900/30 text-red-400 text-xs rounded">Required</span>
                            )}
                            {spec.negate && (
                              <span className="px-2 py-0.5 bg-yellow-900/30 text-yellow-400 text-xs rounded">Negated</span>
                            )}
                          </div>
                          <div className="text-sm text-gray-400">
                            {spec.implementation}
                            {spec.fields.value && (
                              <code className="ml-2 px-2 py-0.5 bg-black text-green-400 rounded text-xs font-mono">
                                {typeof spec.fields.value === 'string' ? spec.fields.value : JSON.stringify(spec.fields.value)}
                              </code>
                            )}
                            {spec.fields.min !== undefined && (
                              <span className="ml-2 text-xs">
                                Min: {spec.fields.min}MB {spec.fields.max !== undefined && `Max: ${spec.fields.max}MB`}
                              </span>
                            )}
                          </div>
                        </div>
                        <button
                          onClick={() => handleRemoveCondition(index)}
                          className="p-2 text-gray-400 hover:text-red-400 hover:bg-red-950/30 rounded transition-colors"
                        >
                          <TrashIcon className="w-4 h-4" />
                        </button>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </div>

            <div className="mt-6 pt-6 border-t border-gray-800 flex items-center justify-end space-x-3">
              <button
                onClick={() => {
                  setShowAddModal(false);
                  setEditingFormat(null);
                }}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={handleSaveFormat}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
              >
                Save
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Add Condition Modal */}
      {showConditionModal && (
        <div className="fixed inset-0 bg-black/90 backdrop-blur-sm z-[60] flex items-center justify-center p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-blue-900/50 rounded-lg p-6 max-w-3xl w-full max-h-[90vh] overflow-y-auto">
            <div className="flex items-center justify-between mb-6">
              <h3 className="text-2xl font-bold text-white">Add Condition</h3>
              <button
                onClick={() => setShowConditionModal(false)}
                className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
              >
                <XMarkIcon className="w-6 h-6" />
              </button>
            </div>

            <div className="p-4 bg-blue-950/30 border border-blue-900/50 rounded-lg mb-6">
              <p className="text-sm text-blue-300">
                Sonarr supports custom conditions against the release properties below.
                <br />
                Visit the wiki for more details: <a href="https://wiki.servarr.com/sonarr/settings#custom-formats" target="_blank" rel="noopener noreferrer" className="text-blue-400 underline">Wiki</a>
              </p>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-6">
              {CONDITION_TYPES.map((type) => (
                <button
                  key={type.value}
                  onClick={() => setConditionForm({ ...conditionForm, implementation: type.value, fields: { value: '' } })}
                  className={`p-4 rounded-lg border-2 transition-all ${
                    conditionForm.implementation === type.value
                      ? 'border-red-600 bg-red-900/20'
                      : 'border-gray-700 bg-gray-800 hover:border-gray-600'
                  }`}
                >
                  <div className="text-white font-medium text-left">{type.label}</div>
                  {type.hasPresets && (
                    <div className="text-xs text-gray-400 mt-1">Custom | Presets</div>
                  )}
                  {!type.hasPresets && conditionForm.implementation === type.value && (
                    <div className="text-xs text-blue-400 mt-1">More Info</div>
                  )}
                </button>
              ))}
            </div>

            <div className="space-y-4">
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Condition Name</label>
                <input
                  type="text"
                  value={conditionForm.name}
                  onChange={(e) => setConditionForm({ ...conditionForm, name: e.target.value })}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-600"
                  placeholder="e.g., 1080p, WEB-DL, etc."
                />
              </div>

              {conditionForm.implementation === 'Size' ? (
                <>
                  <div className="grid grid-cols-2 gap-4">
                    <div>
                      <label className="block text-sm font-medium text-gray-300 mb-2">Min Size (MB)</label>
                      <input
                        type="number"
                        value={conditionForm.fields.min || ''}
                        onChange={(e) => setConditionForm({
                          ...conditionForm,
                          fields: { ...conditionForm.fields, min: parseFloat(e.target.value) || 0 }
                        })}
                        className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-600"
                        placeholder="0"
                      />
                    </div>
                    <div>
                      <label className="block text-sm font-medium text-gray-300 mb-2">Max Size (MB)</label>
                      <input
                        type="number"
                        value={conditionForm.fields.max || ''}
                        onChange={(e) => setConditionForm({
                          ...conditionForm,
                          fields: { ...conditionForm.fields, max: parseFloat(e.target.value) || 0 }
                        })}
                        className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-blue-600"
                        placeholder="Unlimited"
                      />
                    </div>
                  </div>
                </>
              ) : (
                <>
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">
                      {conditionForm.implementation === 'ReleaseTitle' || conditionForm.implementation === 'ReleaseGroup'
                        ? 'Regular Expression'
                        : 'Value'}
                    </label>
                    <input
                      type="text"
                      value={conditionForm.fields.value || ''}
                      onChange={(e) => setConditionForm({
                        ...conditionForm,
                        fields: { value: e.target.value }
                      })}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white font-mono text-sm focus:outline-none focus:border-blue-600"
                      placeholder={
                        conditionForm.implementation === 'ReleaseTitle'
                          ? 'e.g., \\b(1080p|FHD)\\b'
                          : conditionForm.implementation === 'Source'
                          ? 'e.g., BluRay, WEB-DL, HDTV'
                          : conditionForm.implementation === 'Resolution'
                          ? 'e.g., 1080p, 720p, 2160p'
                          : 'Enter value'
                      }
                    />
                  </div>

                  {/* Presets for certain types */}
                  {conditionForm.implementation === 'Source' && SOURCE_PRESETS.length > 0 && (
                    <div>
                      <label className="block text-sm font-medium text-gray-300 mb-2">Presets</label>
                      <div className="flex flex-wrap gap-2">
                        {SOURCE_PRESETS.map((preset) => (
                          <button
                            key={preset}
                            onClick={() => setConditionForm({
                              ...conditionForm,
                              fields: { value: preset }
                            })}
                            className="px-3 py-1 bg-gray-700 hover:bg-gray-600 text-white text-sm rounded transition-colors"
                          >
                            {preset}
                          </button>
                        ))}
                      </div>
                    </div>
                  )}

                  {conditionForm.implementation === 'Resolution' && RESOLUTION_PRESETS.length > 0 && (
                    <div>
                      <label className="block text-sm font-medium text-gray-300 mb-2">Presets</label>
                      <div className="flex flex-wrap gap-2">
                        {RESOLUTION_PRESETS.map((preset) => (
                          <button
                            key={preset}
                            onClick={() => setConditionForm({
                              ...conditionForm,
                              fields: { value: preset }
                            })}
                            className="px-3 py-1 bg-gray-700 hover:bg-gray-600 text-white text-sm rounded transition-colors"
                          >
                            {preset}
                          </button>
                        ))}
                      </div>
                    </div>
                  )}

                  {conditionForm.implementation === 'Language' && LANGUAGE_PRESETS.length > 0 && (
                    <div>
                      <label className="block text-sm font-medium text-gray-300 mb-2">Presets</label>
                      <div className="flex flex-wrap gap-2">
                        {LANGUAGE_PRESETS.map((preset) => (
                          <button
                            key={preset}
                            onClick={() => setConditionForm({
                              ...conditionForm,
                              fields: { value: preset }
                            })}
                            className="px-3 py-1 bg-gray-700 hover:bg-gray-600 text-white text-sm rounded transition-colors"
                          >
                            {preset}
                          </button>
                        ))}
                      </div>
                    </div>
                  )}
                </>
              )}

              <div className="flex items-center space-x-4">
                <label className="flex items-center space-x-2 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={conditionForm.negate}
                    onChange={(e) => setConditionForm({ ...conditionForm, negate: e.target.checked })}
                    className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-yellow-600 focus:ring-yellow-600"
                  />
                  <span className="text-sm text-gray-300">Negate (must NOT match)</span>
                </label>

                <label className="flex items-center space-x-2 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={conditionForm.required}
                    onChange={(e) => setConditionForm({ ...conditionForm, required: e.target.checked })}
                    className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                  />
                  <span className="text-sm text-gray-300">Required</span>
                </label>
              </div>
            </div>

            <div className="mt-6 pt-6 border-t border-gray-800 flex items-center justify-end space-x-3">
              <button
                onClick={() => setShowConditionModal(false)}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
              >
                Close
              </button>
              <button
                onClick={handleAddCondition}
                className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg transition-colors"
              >
                Add Condition
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
