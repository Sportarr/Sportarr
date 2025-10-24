import { useState, useEffect } from 'react';
import { PlusIcon, PencilIcon, TrashIcon, XMarkIcon } from '@heroicons/react/24/outline';

interface ProfilesSettingsProps {
  showAdvanced: boolean;
}

interface QualityProfile {
  id?: number;
  name: string;
  upgradesAllowed: boolean;
  cutoffQuality?: number | null;
  items: QualityItem[];
  formatItems: ProfileFormatItem[];
  minFormatScore?: number | null;
  cutoffFormatScore?: number | null;
  formatScoreIncrement: number;
  minSize?: number | null;
  maxSize?: number | null;
}

interface QualityItem {
  name: string;
  quality: number;
  allowed: boolean;
}

interface ProfileFormatItem {
  formatId: number;
  formatName: string;
  score: number;
}

interface CustomFormat {
  id: number;
  name: string;
}

// Available quality items
const availableQualities: QualityItem[] = [
  { name: 'WEB 2160p', quality: 19, allowed: false },
  { name: 'Bluray-2160p', quality: 18, allowed: false },
  { name: 'Bluray-2160p Remux', quality: 17, allowed: false },
  { name: 'WEB 1080p', quality: 15, allowed: false },
  { name: 'Bluray-1080p', quality: 14, allowed: false },
  { name: 'Bluray-1080p Remux', quality: 13, allowed: false },
  { name: 'HDTV-2160p', quality: 12, allowed: false },
  { name: 'HDTV-1080p', quality: 11, allowed: false },
  { name: 'WEB 720p', quality: 9, allowed: false },
  { name: 'Bluray-720p', quality: 8, allowed: false },
  { name: 'Raw-HD', quality: 7, allowed: false },
  { name: 'WEB 480p', quality: 6, allowed: false },
  { name: 'Bluray-480p', quality: 5, allowed: false },
  { name: 'DVD', quality: 4, allowed: false },
  { name: 'SDTV', quality: 3, allowed: false },
  { name: 'Unknown', quality: 0, allowed: false },
];

export default function ProfilesSettings({ showAdvanced }: ProfilesSettingsProps) {
  const [qualityProfiles, setQualityProfiles] = useState<QualityProfile[]>([]);
  const [customFormats, setCustomFormats] = useState<CustomFormat[]>([]);
  const [editingProfile, setEditingProfile] = useState<QualityProfile | null>(null);
  const [showAddModal, setShowAddModal] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState<number | null>(null);
  const [loading, setLoading] = useState(true);

  // Form state
  const [formData, setFormData] = useState<Partial<QualityProfile>>({
    name: '',
    upgradesAllowed: true,
    cutoffQuality: null,
    items: availableQualities.map(q => ({ ...q })),
    formatItems: [],
    minFormatScore: 0,
    cutoffFormatScore: 10000,
    formatScoreIncrement: 1,
    minSize: null,
    maxSize: null,
  });

  // Load profiles and custom formats
  useEffect(() => {
    loadProfiles();
    loadCustomFormats();
  }, []);

  const loadProfiles = async () => {
    try {
      const response = await fetch('/api/qualityprofile');
      if (response.ok) {
        const data = await response.json();
        setQualityProfiles(data);
      }
    } catch (error) {
      console.error('Failed to load quality profiles:', error);
    } finally {
      setLoading(false);
    }
  };

  const loadCustomFormats = async () => {
    try {
      const response = await fetch('/api/customformat');
      if (response.ok) {
        const data = await response.json();
        setCustomFormats(data);
      }
    } catch (error) {
      console.error('Failed to load custom formats:', error);
    }
  };

  const handleAdd = () => {
    setEditingProfile(null);
    setFormData({
      name: '',
      upgradesAllowed: true,
      cutoffQuality: null,
      items: availableQualities.map(q => ({ ...q })),
      formatItems: customFormats.map(f => ({ formatId: f.id, formatName: f.name, score: 0 })),
      minFormatScore: 0,
      cutoffFormatScore: 10000,
      formatScoreIncrement: 1,
      minSize: null,
      maxSize: null,
    });
    setShowAddModal(true);
  };

  const handleEdit = (profile: QualityProfile) => {
    setEditingProfile(profile);
    setFormData(profile);
    setShowAddModal(true);
  };

  const handleSave = async () => {
    if (!formData.name) return;

    try {
      const url = editingProfile ? `/api/qualityprofile/${editingProfile.id}` : '/api/qualityprofile';
      const method = editingProfile ? 'PUT' : 'POST';

      const response = await fetch(url, {
        method,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(formData),
      });

      if (response.ok) {
        await loadProfiles();
        setShowAddModal(false);
        setEditingProfile(null);
      }
    } catch (error) {
      console.error('Failed to save quality profile:', error);
    }
  };

  const handleDelete = async (id: number) => {
    try {
      const response = await fetch(`/api/qualityprofile/${id}`, { method: 'DELETE' });
      if (response.ok) {
        await loadProfiles();
        setShowDeleteConfirm(null);
      }
    } catch (error) {
      console.error('Failed to delete quality profile:', error);
    }
  };

  const handleToggleQuality = (quality: number) => {
    setFormData(prev => ({
      ...prev,
      items: prev.items?.map(item =>
        item.quality === quality ? { ...item, allowed: !item.allowed } : item
      )
    }));
  };

  const handleFormatScoreChange = (formatId: number, score: number) => {
    setFormData(prev => ({
      ...prev,
      formatItems: prev.formatItems?.map(item =>
        item.formatId === formatId ? { ...item, score } : item
      )
    }));
  };

  const getQualityName = (quality: number | null | undefined) => {
    if (!quality) return 'Not Set';
    const item = availableQualities.find(q => q.quality === quality);
    return item?.name || 'Unknown';
  };

  if (loading) {
    return (
      <div className="max-w-6xl mx-auto text-center py-12">
        <div className="text-gray-400">Loading profiles...</div>
      </div>
    );
  }

  return (
    <div className="max-w-6xl mx-auto">
      <div className="mb-8">
        <h2 className="text-3xl font-bold text-white mb-2">Quality Profiles</h2>
        <p className="text-gray-400">
          Quality profiles determine which releases Fightarr will download and upgrade
        </p>
      </div>

      {/* Quality Profiles List */}
      <div className="mb-8 bg-gradient-to-br from-gray-900 to-black border border-red-900/30 rounded-lg p-6">
        <div className="flex items-center justify-between mb-6">
          <div>
            <h3 className="text-xl font-semibold text-white">Profiles</h3>
            <p className="text-sm text-gray-400 mt-1">
              Configure quality settings and custom format scoring
            </p>
          </div>
          <button
            onClick={handleAdd}
            className="flex items-center px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
          >
            <PlusIcon className="w-4 h-4 mr-2" />
            Add Profile
          </button>
        </div>

        {qualityProfiles.length === 0 ? (
          <div className="text-center py-12 text-gray-500">
            <p className="mb-2">No quality profiles configured</p>
            <p className="text-sm">Create your first profile to get started</p>
          </div>
        ) : (
          <div className="space-y-3">
            {qualityProfiles.map((profile) => (
              <div
                key={profile.id}
                className="group bg-black/30 border border-gray-800 hover:border-red-900/50 rounded-lg p-4 transition-all"
              >
                <div className="flex items-center justify-between">
                  <div className="flex-1">
                    <div className="flex items-center space-x-3 mb-2">
                      <h4 className="text-lg font-semibold text-white">{profile.name}</h4>
                      {profile.upgradesAllowed && (
                        <span className="px-2 py-0.5 bg-green-900/30 text-green-400 text-xs rounded">
                          Upgrades Allowed
                        </span>
                      )}
                    </div>
                    <div className="flex items-center space-x-6 text-sm text-gray-400">
                      <div>
                        <span className="text-gray-500">Cutoff:</span>{' '}
                        <span className="text-white">{getQualityName(profile.cutoffQuality)}</span>
                      </div>
                      <div>
                        <span className="text-gray-500">Qualities:</span>{' '}
                        <span className="text-white">
                          {profile.items.filter(q => q.allowed).length} enabled
                        </span>
                      </div>
                      {showAdvanced && (
                        <div>
                          <span className="text-gray-500">Format Score:</span>{' '}
                          <span className="text-white">{profile.minFormatScore} - {profile.cutoffFormatScore}</span>
                        </div>
                      )}
                    </div>
                  </div>
                  <div className="flex items-center space-x-2">
                    <button
                      onClick={() => handleEdit(profile)}
                      className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
                      title="Edit"
                    >
                      <PencilIcon className="w-5 h-5" />
                    </button>
                    <button
                      onClick={() => setShowDeleteConfirm(profile.id!)}
                      className="p-2 text-gray-400 hover:text-red-400 hover:bg-red-950/30 rounded transition-colors"
                      title="Delete"
                    >
                      <TrashIcon className="w-5 h-5" />
                    </button>
                  </div>
                </div>

                {/* Quality Items */}
                <div className="mt-4 pt-4 border-t border-gray-800">
                  <div className="grid grid-cols-3 gap-2">
                    {profile.items.filter(i => i.allowed).map((item) => (
                      <div
                        key={item.quality}
                        className="px-3 py-2 rounded text-sm bg-green-950/30 text-green-400 border border-green-900/50"
                      >
                        ✓ {item.name}
                      </div>
                    ))}
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Edit/Add Modal */}
      {showAddModal && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4 overflow-y-auto">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-4xl w-full my-8">
            <div className="flex items-center justify-between mb-6">
              <h3 className="text-2xl font-bold text-white">
                {editingProfile ? `Edit ${editingProfile.name}` : 'Add Quality Profile'}
              </h3>
              <button
                onClick={() => setShowAddModal(false)}
                className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded transition-colors"
              >
                <XMarkIcon className="w-6 h-6" />
              </button>
            </div>

            <div className="space-y-6 max-h-[70vh] overflow-y-auto pr-2">
              {/* Name */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Name *</label>
                <input
                  type="text"
                  value={formData.name || ''}
                  onChange={(e) => setFormData(prev => ({ ...prev, name: e.target.value }))}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                  placeholder="4K Quality"
                />
              </div>

              {/* Upgrades Allowed */}
              <label className="flex items-center space-x-3 cursor-pointer">
                <input
                  type="checkbox"
                  checked={formData.upgradesAllowed || false}
                  onChange={(e) => setFormData(prev => ({ ...prev, upgradesAllowed: e.target.checked }))}
                  className="w-4 h-4 rounded border-gray-600 bg-gray-800 text-red-600 focus:ring-red-600"
                />
                <span className="text-sm font-medium text-gray-300">
                  Upgrades Allowed (If disabled qualities will not be upgraded)
                </span>
              </label>

              {/* Quality Selection */}
              <div>
                <h4 className="text-lg font-semibold text-white mb-3">Qualities</h4>
                <p className="text-sm text-gray-400 mb-3">
                  Qualities higher in the list are more preferred. Qualities within the same group are equal. Only checked qualities are wanted.
                </p>
                <div className="grid grid-cols-2 md:grid-cols-3 gap-2 max-h-64 overflow-y-auto p-2 bg-black/30 rounded-lg">
                  {formData.items?.map((item) => (
                    <button
                      key={item.quality}
                      onClick={() => handleToggleQuality(item.quality)}
                      className={`px-3 py-2 rounded text-sm text-left transition-all ${
                        item.allowed
                          ? 'bg-green-950/30 text-green-400 border border-green-900/50'
                          : 'bg-gray-900/50 text-gray-500 border border-gray-800 hover:border-gray-700'
                      }`}
                    >
                      {item.allowed ? '✓' : '○'} {item.name}
                    </button>
                  ))}
                </div>
              </div>

              {/* Upgrade Until */}
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">Upgrade Until</label>
                <select
                  value={formData.cutoffQuality || ''}
                  onChange={(e) => setFormData(prev => ({ ...prev, cutoffQuality: parseInt(e.target.value) || null }))}
                  className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                >
                  <option value="">Select upgrade cutoff...</option>
                  {formData.items?.filter(q => q.allowed).map(q => (
                    <option key={q.quality} value={q.quality}>{q.name}</option>
                  ))}
                </select>
                <p className="text-xs text-gray-500 mt-1">
                  Once this quality is reached Sonarr will no longer download episodes
                </p>
              </div>

              {/* Custom Format Scoring */}
              <div className="space-y-4 p-4 bg-purple-950/10 border border-purple-900/30 rounded-lg">
                <h4 className="text-lg font-semibold text-white">Custom Formats</h4>
                <p className="text-sm text-gray-400">
                  Sonarr scores each release using the sum of scores for matching custom formats. If a new release would improve the score, at the same or better quality, then Sonarr will grab it.
                </p>

                <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">
                      Minimum Custom Format Score
                    </label>
                    <input
                      type="number"
                      value={formData.minFormatScore || 0}
                      onChange={(e) => setFormData(prev => ({ ...prev, minFormatScore: parseInt(e.target.value) || 0 }))}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <p className="text-xs text-gray-500 mt-1">
                      Minimum custom format score allowed to download
                    </p>
                  </div>

                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">
                      Upgrade Until Custom Format Score
                    </label>
                    <input
                      type="number"
                      value={formData.cutoffFormatScore || 10000}
                      onChange={(e) => setFormData(prev => ({ ...prev, cutoffFormatScore: parseInt(e.target.value) || 10000 }))}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <p className="text-xs text-gray-500 mt-1">
                      Once this custom format score is reached Sonarr will no longer grab episode releases
                    </p>
                  </div>

                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-2">
                      Minimum Custom Format Score Increment
                    </label>
                    <input
                      type="number"
                      value={formData.formatScoreIncrement || 1}
                      onChange={(e) => setFormData(prev => ({ ...prev, formatScoreIncrement: parseInt(e.target.value) || 1 }))}
                      className="w-full px-4 py-2 bg-gray-800 border border-gray-700 rounded-lg text-white focus:outline-none focus:border-red-600"
                    />
                    <p className="text-xs text-gray-500 mt-1">
                      Minimum required improvement of the custom format score between existing and new releases before Sonarr considers it an upgrade
                    </p>
                  </div>
                </div>

                {/* Custom Formats List */}
                {formData.formatItems && formData.formatItems.length > 0 ? (
                  <div className="mt-4">
                    <h5 className="text-md font-semibold text-white mb-2">Custom Format</h5>
                    <div className="max-h-64 overflow-y-auto space-y-2 p-3 bg-black/30 rounded-lg">
                      {formData.formatItems.map((item) => (
                        <div key={item.formatId} className="flex items-center justify-between p-2 bg-gray-800/50 rounded hover:bg-gray-800 transition-colors">
                          <span className="text-white font-medium">{item.formatName}</span>
                          <div className="flex items-center space-x-2">
                            <input
                              type="number"
                              value={item.score}
                              onChange={(e) => handleFormatScoreChange(item.formatId, parseInt(e.target.value) || 0)}
                              className="w-24 px-3 py-1 bg-gray-900 border border-gray-700 rounded text-white text-center focus:outline-none focus:border-purple-600"
                              placeholder="0"
                            />
                            <span className={`text-xs px-2 py-1 rounded min-w-[60px] text-center ${
                              item.score > 0
                                ? 'bg-green-900/30 text-green-400'
                                : item.score < 0
                                  ? 'bg-red-900/30 text-red-400'
                                  : 'bg-gray-700 text-gray-400'
                            }`}>
                              {item.score > 0 ? '+' : ''}{item.score}
                            </span>
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>
                ) : (
                  <div className="p-6 bg-black/30 rounded-lg text-center">
                    <p className="text-gray-500 mb-2">No custom formats configured</p>
                    <p className="text-sm text-gray-400">
                      Create custom formats in Settings → Custom Formats to enable scoring
                    </p>
                  </div>
                )}
              </div>
            </div>

            <div className="mt-6 pt-6 border-t border-gray-800 flex items-center justify-end space-x-3">
              <button
                onClick={() => setShowAddModal(false)}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={handleSave}
                disabled={!formData.name}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed text-white rounded-lg transition-colors"
              >
                Save
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Delete Confirmation */}
      {showDeleteConfirm !== null && (
        <div className="fixed inset-0 bg-black/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
          <div className="bg-gradient-to-br from-gray-900 to-black border border-red-900/50 rounded-lg p-6 max-w-md w-full">
            <h3 className="text-2xl font-bold text-white mb-4">Delete Profile?</h3>
            <p className="text-gray-400 mb-6">
              Are you sure you want to delete this quality profile? This action cannot be undone.
            </p>
            <div className="flex items-center justify-end space-x-3">
              <button
                onClick={() => setShowDeleteConfirm(null)}
                className="px-4 py-2 bg-gray-800 hover:bg-gray-700 text-white rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                onClick={() => handleDelete(showDeleteConfirm)}
                className="px-4 py-2 bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
              >
                Delete
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
